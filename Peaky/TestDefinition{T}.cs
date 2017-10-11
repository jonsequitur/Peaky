// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Peaky
{
    internal class TestDefinition<T> : TestDefinition
        where T : class, IPeakyTest
    {
        private readonly Func<T, dynamic> defaultExecuteTestMethod;
        private readonly MethodInfo methodInfo;

        internal TestDefinition(MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException(nameof(methodInfo));
            }

            this.methodInfo = methodInfo;
            defaultExecuteTestMethod = BuildTestMethodExpression(methodInfo, methodInfo.GetParameters().Select(p => Expression.Constant(p.DefaultValue)));
        }

        public override string TestName => methodInfo.Name;

        internal override dynamic Run(ActionContext context, Func<Type, object> resolver)
        {
            var executeTestMethod = defaultExecuteTestMethod;
            var methodParameters = methodInfo.GetParameters();

            var queryParameters = context.HttpContext.Request.Query;

            if (queryParameters.Keys.Any(p => methodParameters.Select(pp => pp.Name).Contains(p)))
            {
                executeTestMethod = BuildTestMethodExpression(methodInfo,
                                                              methodParameters
                                                                  .Select(p =>
                                                                  {
                                                                      var value = queryParameters[p.Name].FirstOrDefault() ??
                                                                                  p.DefaultValue;
                                                                      try
                                                                      {
                                                                          var castedValue = Convert.ChangeType(value, p.ParameterType);
                                                                          return Expression.Constant(castedValue);
                                                                      }
                                                                      catch (FormatException e)
                                                                      {
                                                                          throw new MonitorParameterFormatException(p.Name, p.ParameterType, e);
                                                                      }
                                                                  }));
            }

            var testClassInstance = (T) resolver(typeof(T));

            return executeTestMethod(testClassInstance);
        }

        public override string ToString()
        {
            return $"{base.ToString()} ({methodInfo.DeclaringType}.{methodInfo.Name})";
        }

        private static Func<T, dynamic> BuildTestMethodExpression(MethodInfo methodInfo, IEnumerable<ConstantExpression> parameters)
        {
            var test = Expression.Parameter(typeof(T), "test");

            if (methodInfo.ReturnType != typeof(void))
            {
                if (methodInfo.ReturnType.GetTypeInfo().IsClass)
                {
                    return Expression.Lambda<Func<T, dynamic>>(Expression.Call(test,
                                                                               methodInfo,
                                                                               parameters),
                                                               test).Compile();
                }
                dynamic testMethod = Expression.Lambda(typeof(Func<,>)
                                                           .MakeGenericType(typeof(T), methodInfo.ReturnType),
                                                       Expression.Call(test,
                                                                       methodInfo,
                                                                       parameters),
                                                       test).Compile();

                return testClassInstance => testMethod(testClassInstance);
            }

            var voidRunMethod = Expression.Lambda<Action<T>>(Expression.Call(test,
                                                                             methodInfo,
                                                                             parameters),
                                                             test).Compile();

            return testClassInstance =>
            {
                voidRunMethod(testClassInstance);
                return new object();
            };
        }
    }
}