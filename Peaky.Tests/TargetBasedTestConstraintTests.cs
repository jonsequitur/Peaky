// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Pocket;
using Xunit;
using Xunit.Abstractions;

namespace Peaky.Tests
{
    public class TargetBasedTestConstraintTests
    {
        private readonly CompositeDisposable disposables;

        public TargetBasedTestConstraintTests(ITestOutputHelper output)
        {
            disposables = new CompositeDisposable
            {
                LogEvents.Subscribe(e => output.WriteLine(e.ToLogString()))
            };
        }

        private HttpClient CreateApiClient(Func<HttpClient, bool> buildChecker)
        {
            var testApi = new PeakyService(targets =>
                                          targets.Add("staging",
                                                      "widgetapi",
                                                      new Uri("http://staging.widgets.com"),
                                                      dependencies =>
                                                          dependencies
                                                              .Register(() => buildChecker)),
                                      testTypes: typeof(TestsConstrainedToTarget));

            disposables.Add(testApi);
            return testApi.CreateHttpClient();
        }

        public void Dispose() => disposables.Dispose();

        [Fact]
        public async Task A_test_can_be_hidden_based_on_the_target_application_build_date_sensor()
        {
            var response = await CreateApiClient(BuildDateAfter(DateTime.Now.AddDays(10))).GetAsync("http://tests.com/tests");

            JArray tests = response.JsonContent().Tests;

            tests.Should().NotContain(t => t.Value<string>("Url").Contains("target_based_constraint_test"));
        }

        [Fact]
        public async Task A_test_can_be_shown_based_on_the_target_application_build_date_sensor()
        {
            var response = await CreateApiClient(BuildDateAfter(DateTime.Now.Subtract(TimeSpan.FromDays(1000)))).GetAsync("http://tests.com/tests");

            JArray tests = response.JsonContent().Tests;

            tests.Should().Contain(t => t.Value<string>("Url").Contains("target_based_constraint_test"));
        }

        [Fact]
        public async Task Target_constraints_cache_their_results_and_and_do_not_trigger_every_time_Match_is_called()
        {
            var constraintCalls = 0;

            var apiClient = CreateApiClient(_ =>
            {
                Interlocked.Increment(ref constraintCalls);
                return false;
            });

            await apiClient.GetAsync("http://tests.com/tests");
            await apiClient.GetAsync("http://tests.com/tests");
            await apiClient.GetAsync("http://tests.com/tests");

            constraintCalls.Should().Be(1);
        }

        private static Func<HttpClient, bool> BuildDateAfter(DateTime buildDateAfter)
        {
            return httpClient =>
            {
                var sensorResult = httpClient.GetAsync("/sensors").Result.JsonContent();
                Console.WriteLine(new { sensorResult });
                DateTime buildDate = sensorResult.Version["Build date"];
                Console.WriteLine(new { buildDate, buildDateAfter });
                return buildDate > buildDateAfter;
            };
        }

        private class TestsConstrainedToTarget : IApplyToTarget
        {
            private readonly HttpClient httpClient;
            private readonly Func<HttpClient, bool> buildChecker;

            public TestsConstrainedToTarget(HttpClient httpClient, Func<HttpClient, bool> buildChecker)
            {
                if (httpClient == null)
                {
                    throw new ArgumentNullException(nameof(httpClient));
                }
                this.httpClient = httpClient;
                this.buildChecker = buildChecker;
            }

            public void target_based_constraint_test()
            {
            }

            public bool AppliesToTarget(TestTarget target)
            {
                return buildChecker?.Invoke(httpClient) ?? false;
            }
        }
    }
}
