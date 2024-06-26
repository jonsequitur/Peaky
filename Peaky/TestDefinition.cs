// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Http;

namespace Peaky;

public abstract class TestDefinition
{
    private IEnumerable<TestParameter> testParameters;

    public string TestName { get; internal set; }

    public virtual string[] Tags { get; set; } = Array.Empty<string>();

    internal Type TestType { get;  set; }

    internal MethodInfo TestMethod { get; set; }

    internal abstract object Run(HttpContext httpContext, Func<Type, object> resolve, TestTarget target);

    internal static TestDefinition Create(MethodInfo methodInfo)
    {
        var testType = methodInfo.DeclaringType;
        var testDefinitionType = typeof(TestDefinition<>).MakeGenericType(testType);
        var testDefinition = (TestDefinition) Activator.CreateInstance(
            testDefinitionType,
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new object[] { methodInfo },
            null);
        testDefinition.TestMethod = methodInfo;
        testDefinition.TestType = testType;
        testDefinition.Parameters = methodInfo.GetParameters()
                                              .Select(p =>
                                                          new TestParameter(p.Name, p.GetDefaultValue()));
        return testDefinition;
    }

    internal IEnumerable<TestParameter> Parameters
    {
        get => testParameters ??= Enumerable.Empty<TestParameter>();
        set => testParameters = value;
    }

    public abstract bool AppliesTo(TestTarget target);
}