using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Peaky
{
    internal static class DelegateExtensions
    {
        private static readonly ConcurrentDictionary<MethodInfo, AnonymousMethodInfo> anonymousMethodInfos =
            new ConcurrentDictionary<MethodInfo, AnonymousMethodInfo>();

        public static AnonymousMethodInfo GetAnonymousMethodInfo<T>(this Func<T> anonymousMethod) =>
            anonymousMethodInfos.GetOrAdd(anonymousMethod.GetMethodInfo(), m => new AnonymousMethodInfo<T>(anonymousMethod));

        public static AnonymousMethodInfo GetAnonymousMethodInfo(this Delegate anonymousMethod) =>
            anonymousMethodInfos.GetOrAdd(anonymousMethod.GetMethodInfo(), m => new AnonymousMethodInfo(anonymousMethod.GetMethodInfo()));
    }
}
