// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
using FluentAssertions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Its.Recipes;
using Newtonsoft.Json;
using Peaky.Tests.TestClasses;
using Pocket;
using Xunit;
using Xunit.Abstractions;

namespace Peaky.Tests;

public class TestDiscoveryTests : IDisposable
{
    private readonly HttpClient apiClient;
    private readonly CompositeDisposable disposables = new();

    public TestDiscoveryTests(ITestOutputHelper output)
    {
        disposables.Add(LogEvents.Subscribe(e => output.WriteLine(e.ToLogString())));

        var peakyService = new PeakyService(
            targets =>
                targets.Add("staging", "widgetapi", new Uri("http://staging.widgets.com"))
                       .Add("production", "widgetapi", new Uri("http://widgets.com"))
                       .Add("staging", "sprocketapi", new Uri("http://staging.sprockets.com"))
                       .Add("staging", "parameterized", new Uri("http://staging.parameterized.com"))
                       .Add("production", "sprocketapi", new Uri("http://sprockets.com")));

        apiClient = peakyService.CreateHttpClient();

        disposables.Add(peakyService);
    }

    public void Dispose() => disposables.Dispose();

    [Fact]
    public async Task API_exposes_void_methods()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi");

        response.ShouldSucceed();

        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("passing_void_test");
    }

    [Fact]
    public async Task API_does_not_expose_private_methods()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi");

        response.ShouldSucceed();

        var json = await response.Content.ReadAsStringAsync();

        json.Should().NotContain("not_a_test");
    }

    [Fact]
    public async Task API_does_not_expose_static_methods()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi");

        response.ShouldSucceed();

        var json = await response.Content.ReadAsStringAsync();

        json.Should().NotContain("not_a_test");
    }

    [Fact]
    public async Task API_does_not_expose_environments_that_were_not_configured()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/" + Any.CamelCaseName());

        response.ShouldFailWith(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task API_does_not_expose_applications_that_were_not_configured()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production/" + Any.CamelCaseName());

        response.ShouldFailWith(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_tests_are_queried_by_environment_then_tests_not_valid_for_that_enviroment_are_not_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production/widgetapi");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        testList.Tests.Should().NotContain(o => o.Url == "internal_only_test");
    }

    [Fact]
    public async Task When_tests_are_queried_by_application_then_tests_not_valid_for_that_application_are_not_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production/sprocketapi/");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        testList.Tests
                .Should().NotContain(o => o.Url.Contains("widgetapi_only_test"));
    }

    [Fact]
    public async Task Tests_can_be_queried_by_application_across_all_environments()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/widgetapi/");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        testList.Tests
                .Should()
                .NotContain(o => o.Url.Contains("sprocketapi"));
        testList.Tests
                .Should()
                .Contain(o => o.Url == "http://blammo.com/tests/staging/widgetapi/is_reachable");
        testList.Tests
                .Should()
                .Contain(o => o.Url == "http://blammo.com/tests/production/widgetapi/is_reachable");
    }

    [Fact]
    public async Task When_tests_are_queried_by_environment_then_routes_are_returned_with_the_environment_filled_in()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production/widgetapi");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        testList.Tests
                .Should().Contain(o => o.Url == "http://blammo.com/tests/production/widgetapi/passing_test_returns_object");
    }

    [Fact]
    public async Task When_two_tests_have_the_same_name_then_an_informative_error_URL_is_displayed()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production/widgetapi");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        testList.Tests
                .Should()
                .Contain(o =>
                             o.Url == "http://blammo.com/tests/production/widgetapi/name_collision__1");
    }

    [Fact]
    public async Task When_an_undefined_application_is_queried_then_an_informative_error_is_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production/widgetapiz/is_reachable");

        response.ShouldFailWith(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_an_undefined_environment_is_queried_then_an_informative_error_is_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/productionz/widgetapi/is_reachable");

        response.ShouldFailWith(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_tests_are_queried_by_environment_but_not_application_then_tests_for_all_applications_are_shown()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/production");

        var testList = await response.AsTestList();

        var tests = testList.Tests;

        tests.Should()
             .Contain(o =>
                          o.Url == "http://blammo.com/tests/production/widgetapi/is_reachable");
        tests.Should()
             .Contain(o =>
                          o.Url == "http://blammo.com/tests/production/sprocketapi/is_reachable");

        tests.Should()
             .NotContain(o =>
                             o.Url == "http://blammo.com/tests/staging/widgetapi/is_reachable");
        tests.Should()
             .NotContain(o =>
                             o.Url == "http://blammo.com/tests/staging/sprocketapi/is_reachable");
    }

    [Fact]
    public async Task When_tests_are_queried_with_no_environment_or_application_then_all_tests_are_shown()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        var tests = testList.Tests;

        tests.Should()
             .Contain(o =>
                          o.Url == "http://blammo.com/tests/production/widgetapi/is_reachable");
        tests.Should()
             .Contain(o =>
                          o.Url == "http://blammo.com/tests/production/sprocketapi/is_reachable");

        tests.Should()
             .Contain(o =>
                          o.Url == "http://blammo.com/tests/staging/widgetapi/is_reachable");
        tests.Should()
             .Contain(o =>
                          o.Url == "http://blammo.com/tests/staging/sprocketapi/is_reachable");
    }

    [Fact]
    public async Task Specific_tests_can_be_routed_using_the_testTypes_argument()
    {
        var api = new PeakyService(targets =>
                                       targets.Add("production",
                                                   "widgetapi",
                                                   new Uri("http://widgets.com")),
                                   testTypes: new[] { typeof(WidgetApiTests) });

        var response = await api.CreateHttpClient().GetAsync("http://blammo.com/tests/");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        testList.Tests
                .Should()
                .Contain(o =>
                             o.Url.Contains("widgetapi_only_test"));
        testList.Tests
                .Should()
                .NotContain(o =>
                                o.Url.Contains("passing_test_returns_object"));
    }

    [Category("Tags")]
    [Fact]
    public async Task when_tests_with_a_specific_tag_are_requested_then_tests_with_that_tag_are_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=true");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("honeycrisp");
    }

    [Category("Tags")]
    [Fact]
    public async Task when_tests_with_a_specific_tag_are_requested_then_untagged_tests_are_not_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=true");

        response.ShouldSucceed();

        (await response.Content.ReadAsStringAsync()).Should().NotContain("passing_test");
    }

    [Category("Tags")]
    [Fact]
    public async Task when_tests_with_a_specific_tag_are_requested_then_tagged_tests_without_that_tag_are_not_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=true");

        response.ShouldSucceed();

        (await response.Content.ReadAsStringAsync()).Should().NotContain("tangerine");
    }

    [Category("Tags")]
    [Fact]
    public async Task when_a_tag_is_not_specified_then_tests_with_tags_are_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();

        var tests = (await response.AsTestList()).Tests;

        tests.Should().Contain(test => test.Url.EndsWith("honeycrisp"));
    }

    [Category("Tags")]
    [Fact]
    public async Task when_a_tag_is_specified_that_multiple_tests_share_then_both_are_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?fruit=true");

        response.ShouldSucceed();

        var tests = (await response.AsTestList())                                .Tests;

        tests.Should().Contain(test => test.Url.EndsWith("honeycrisp"));
    }

    [Category("Tags")]
    [Fact]
    public async Task when_tests_with_a_specific_tag_are_requested_to_be_excluded_then_they_are()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=false");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("honeycrisp");
    }

    [Category("Tags")]
    [Fact]
    public async Task when_tests_with_a_specific_tag_are_requested_to_be_excluded_then_tagged_tests_without_that_tag_are_included()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=false");

        response.ShouldSucceed();
        var tests = (await response.AsTestList())                                .Tests;

        tests.Should().Contain(test => test.Url.EndsWith("tangerine"));
    }

    [Category("Tags")]
    [Fact]
    public async Task when_tests_with_a_specific_tag_are_requested_to_be_excluded_then_untagged_tests_are_included()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=false");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("passing_test_returns_object");
    }

    [Category("Tags")]
    [Fact]
    public async Task all_of_the_available_tags_are_exposed_by_the_API()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/");

        response.ShouldSucceed();

        var testList = await response.AsTestList();

        var honeycrisps = testList.Tests.Where(t => t.Url.EndsWith("honeycrisp")).ToArray();

        honeycrisps.Length.Should().BeGreaterThan(0);

        foreach (var honeycrisp in honeycrisps)
        {
            honeycrisp.Tags.Should().Contain(v => v == "apple");
            honeycrisp.Tags.Should().Contain(v => v == "fruit");
        }
    }

    [Category("Tags")]
    [Fact]
    public async Task when_two_tags_are_required_then_only_tests_containing_both_are_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?apple=true&fruit=true");

        response.ShouldSucceed();

        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("honeycrisp");
        json.Should().NotContain("tangerine");
    }

    [Category("Tags")]
    [Fact]
    public async Task when_a_tag_is_specific_the_casing_on_the_tag_does_not_matter()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/?Brooklyn=true");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("manhattan");
    }

    [Fact]
    public async Task when_a_test_class_has_a_public_property_then_it_is_not_discovered_as_a_test()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SomeProperty");
    }

    [Fact]
    public async Task when_a_public_void_method_has_optional_parameters_then_it_is_discovered_as_a_test()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("void_test_with_optional_parameters");
    }

    [Fact]
    public async Task when_public_type_returning_methods_have_optional_parameters_then_they_are_discovered_as_a_tests()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("string_returning_test_with_optional_parameters");
    }

    [Fact]
    public async Task when_a_public_method_has_non_optional_parameters_then_it_is_discovered_as_a_test()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("test_with_non_optional_parameters");
    }

    [Fact]
    public async Task when_a_test_with_optional_parameters_is_called_then_the_parameter_is_set_to_the_default_value()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/" +
                                                "string_returning_test_with_optional_parameters");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("bar");
    }

    [Fact]
    public async Task when_a_test_with_optional_parameters_is_called_then_the_parameters_can_be_set_with_a_query_string_parameter()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?foo=notbar");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("notbar");
    }

    [Fact]
    public async Task when_a_test_with_optional_parameters_is_called_with_encoded_values_then_values_are_decoded()
    {
        var response = await apiClient.GetAsync($"http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?foo={HttpUtility.UrlEncode("//")}");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("//");
    }

    [Theory]
    [InlineData("http://blammo.com/tests/staging/parameterized/I_do_stuff?expectedResult=true&testCaseId=case5")]
    [InlineData("http://blammo.com/tests/staging/parameterized/I_do_stuff_and_return_bool?expectedResult=true&testCaseId=case6")]
    [InlineData("http://blammo.com/tests/staging/parameterized/I_do_stuff_and_return_task?expectedResult=false&testCaseId=case8")]
    [InlineData("http://blammo.com/tests/staging/parameterized/I_do_stuff_and_return_task_of_bool/?expectedResult=false&testCaseId=case9")]
    [InlineData("http://blammo.com/tests/staging/parameterized/I_use_enum?value=valueone")]
    [InlineData("http://blammo.com/tests/staging/parameterized/I_use_enum?value=valueTwo")]
    public async Task when_a_testcase_is_called_then_the_test_will_execute(string url)
    {
        var  response = await apiClient.GetAsync(url);
        response.ShouldSucceed();
    }

    [Fact]
    public async Task when_passing_query_parameters_that_are_not_test_parameters_then_the_test_still_executes()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?foo=notbar&apikey=iHaveNothingToDoWithYourTest");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("notbar");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("iHaveNothingToDoWithYourTest");
    }

    [Fact]
    public async Task when_a_parameter_of_the_wrong_type_is_supplied_then_a_useful_error_message_is_returned()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/" +
                                                "string_returning_test_with_optional_parameters?count=gronk");

        response.ShouldFailWith(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).Should().Contain("The value specified for parameter 'count' could not be parsed as System.Int32");
    }

    [Fact]
    public async Task when_the_first_optional_parameter_is_not_specified_then_the_test_still_works()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?count=5");

        response.ShouldSucceed();
        (await response.Content.ReadAsStringAsync()).Should().Contain("bar");
        (await response.Content.ReadAsStringAsync()).Should().Contain("5");
    }

    [Fact]
    public async Task when_tests_with_parameters_are_called_repeatedly_with_different_parameters_then_the_different_parameters_are_used()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?foo=1");
        (await response.Content.ReadAsStringAsync()).Should().Contain("1");

        response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters");
        (await response.Content.ReadAsStringAsync()).Should().Contain("bar");

        response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?foo=final");
        (await response.Content.ReadAsStringAsync()).Should().Contain("final");
    }

    [Fact]
    public async Task when_a_test_accepts_input_parameters_then_the_input_parameter_names_are_discoverable()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());

        var test = content.Tests.Single(t => t.Url == "http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?count=1&foo=bar");
        test.Parameters.Should().ContainSingle(p => p.Name == "foo");
        test.Parameters.Should().ContainSingle(p => p.Name == "count");
    }

    [Fact]
    public async Task when_a_test_accepts_input_parameters_then_the_input_parameter_default_values_are_discoverable()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());

        var test = content.Tests.Single(t => t.Url == "http://blammo.com/tests/staging/widgetapi/string_returning_test_with_optional_parameters?count=1&foo=bar");
        test.Parameters.Single(p => p.Name == "foo").DefaultValue.Should().BeEquivalentTo("bar");
        test.Parameters.Single(p => p.Name == "count").DefaultValue.Should().BeEquivalentTo(1);
    }

    [Fact]
    public async Task when_a_test_does_not_accept_input_parameters_then_queryParameters_is_empty()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/widgetapi/");

        response.ShouldSucceed();
        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());

        content.Tests.Single(t => t.Url == "http://blammo.com/tests/staging/widgetapi/passing_test_returns_object")
               .Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task when_a_test_exposes_parameterized_test_cases_then_the_input_parameters_are_recorded()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/Parameterized/");

        response.ShouldSucceed();
        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());
            
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff?expectedResult=true&testCaseId=case1", StringComparison.OrdinalIgnoreCase));
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff?expectedResult=false&testCaseId=case2", StringComparison.OrdinalIgnoreCase));
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff?expectedResult=true&testCaseId=case3", StringComparison.OrdinalIgnoreCase));
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff?expectedResult=false&testCaseId=case4", StringComparison.OrdinalIgnoreCase));
            
    }

    [Fact]
    public async Task when_a_test_exposes_async_parameterized_test_cases_with_return_values_then_the_input_parameters_are_recorded()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/Parameterized/");

        response.ShouldSucceed();
        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff_and_return_bool?expectedResult=true&testCaseId=case6", StringComparison.OrdinalIgnoreCase));
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff_and_return_bool?expectedResult=false&testCaseId=case7", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task when_a_test_exposes_async_parameterized_test_cases_then_the_input_parameters_are_recorded()
    {
        var response = await apiClient.GetAsync("http://blammo.com/tests/staging/Parameterized/");

        response.ShouldSucceed();
        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff_and_return_task?expectedResult=false&testCaseId=case8", StringComparison.OrdinalIgnoreCase));
        content.Tests.Should().Contain(t => t.Url.ToString().EndsWith("I_do_stuff_and_return_task_of_bool?expectedResult=false&testCaseId=case9", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tests_URL_scheme_can_be_configured_to_differ_from_the_default()
    {
        using var peakyService = new PeakyService(
            targets =>
                targets.Add("production", "example", new Uri("https://example.com")));

        var apiClient = peakyService.CreateHttpClient();

        var response = await apiClient.GetAsync("http://example.com/tests");

        var content = JsonConvert.DeserializeObject<TestDiscoveryResponse>(await response.Content.ReadAsStringAsync());

        content.Tests.Should().Contain(o => o.Url == "https://example.com/tests/production/example/passing_test_returns_object");
    }
}