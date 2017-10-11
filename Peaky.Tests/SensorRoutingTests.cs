// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Its.Recipes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Peaky.Tests
{
    public class SensorRoutingTests : IDisposable
    {
        private static HttpClient apiClient;
        private string sensorName;

        public SensorRoutingTests()
        {
            // FIX: (SensorRoutingTests) 
//            configuration = new HttpConfiguration();
//            configuration.MapSensorRoutes(ctx => true);
//            var server = new HttpServer(configuration);
//            apiClient = new HttpClient(server);
//            configuration.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Default;
//            sensorName = Any.AlphanumericString(10, 20);
        }

        public void Dispose()
        {
            DiagnosticSensor.Remove(sensorName);
            TestSensor.GetSensorValue = null;
        }

        [Fact]
        public void Sensors_are_available_by_name()
        {
            var words = Any.Paragraph(5);

            DiagnosticSensor.Register(() => new { words }, sensorName);

            var response = apiClient.GetStringAsync("http://blammo.com/sensors/" + sensorName).Result;

            Console.WriteLine(response);

            response.Should().Contain(words);
        }

        [Fact]
        public void All_sensors_can_be_queried_at_once()
        {
            var value = Any.AlphanumericString(20, 100);
            TestSensor.GetSensorValue = () => value;

            var response = apiClient.GetStringAsync("http://blammo.com/sensors/").Result;

            Console.WriteLine(response);

            response.Should().Contain(value);
        }

        [Fact]
        public void Sensor_root_content_includes_link_to_sensors_root()
        {
            var response = apiClient.GetStringAsync("http://blammo.com/sensors/").Result;

            dynamic sensorValue = JObject.Parse(response);
            Console.WriteLine(response);
            string selfLink = sensorValue._links.self;

            selfLink.Should().Be("/sensors/");
        }

        [Fact]
        public void Sensor_root_content_contains_links_to_all_sensors()
        {
            dynamic result = JObject.Parse(apiClient.GetStringAsync("http://blammo.com/sensors/").Result);

            Console.WriteLine(result);

            ((string) result._links.Application)
                .Should().Be("/sensors/application");
        }

        [Fact]
        public void Specific_sensor_content_includes_link_to_sensor_when_sensor_is_dictionary()
        {
            DiagnosticSensor.Register(() => "hello!", sensorName);
            var response = apiClient.GetStringAsync("http://blammo.com/sensors/" + sensorName).Result;

            dynamic sensorValue = JObject.Parse(response);
            Console.WriteLine(response);
            string selfLink = sensorValue._links.self;

            selfLink.Should().Be("/sensors/" + sensorName);
        }

        [Fact]
        public void Specific_sensor_content_includes_link_to_sensor_when_sensor_is_object()
        {
            DiagnosticSensor.Register(() => new FileInfo("c:\\temp\\foo.txt"), sensorName);
            var response = apiClient.GetStringAsync("http://blammo.com/sensors/" + sensorName).Result;

            dynamic sensorValue = JObject.Parse(response);
            Console.WriteLine(response);
            string selfLink = sensorValue._links.self;

            selfLink.Should().Be("/sensors/" + sensorName);
        }

        [Fact]
        public void When_a_sensor_name_is_unknown_then_the_api_returns_404()
        {
            var sensorName = Guid.NewGuid();

            var response = apiClient.GetAsync("http://blammo.com/sensors/" + sensorName);

            response.Result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        [Fact]
        public void Sensor_routes_are_case_insensitive()
        {
            apiClient.GetAsync("http://blammo.com/sensors/application").Result
                     .StatusCode
                     .Should()
                     .Be(HttpStatusCode.OK);
        }

        [Fact]
        public void Sensors_that_return_arrays_are_correctly_serialized()
        {
            var value = new
            {
                Name = Any.FullName(),
                Email = Any.Email()
            };
            TestSensor.GetSensorValue = () => new object[] { value };

            var response = apiClient.GetAsync("http://blammo.com/sensors/sensormethod").Result;

            response.ShouldSucceed();

            Console.WriteLine(response);

            var values = response.JsonContent();
            string name = values[0].Name;
            string email = values[0].Email;

            name.Should().Be(value.Name);
            email.Should().Be(value.Email);
        }

        [Fact]
        public async Task When_one_sensor_throws_then_other_sensors_are_not_affected()
        {
            TestSensor.GetSensorValue = () => throw new Exception("oops!");

            var response = await apiClient.GetAsync("http://blammo.com/sensors/sensormethod");

            response.ShouldFailWith(HttpStatusCode.InternalServerError);

            var body = await response.Content.ReadAsStringAsync();

            body.Should().Contain("oops!");
            body.Should().Contain(@"""_links"":");
        }
    }
}
