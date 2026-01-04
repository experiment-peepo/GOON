using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TrainMeX.Classes {
    /// <summary>
    /// Simple service container for dependency injection
    /// </summary>
    public class ServiceContainer {
        internal static readonly ConcurrentDictionary<Type, object> _services = new ConcurrentDictionary<Type, object>();

        /// <summary>
        /// Registers a service instance
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <param name="service">Service instance</param>
        public static void Register<T>(T service) {
            if (service == null) {
                throw new ArgumentNullException(nameof(service));
            }
            _services[typeof(T)] = service;
        }

        /// <summary>
        /// Clears all registered services
        /// </summary>
        public static void Clear() {
            _services.Clear();
        }

        /// <summary>
        /// Gets a registered service
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <returns>Service instance</returns>
        /// <exception cref="InvalidOperationException">Thrown if service is not registered</exception>
        public static T Get<T>() {
            var type = typeof(T);
            if (!_services.TryGetValue(type, out var service)) {
                throw new InvalidOperationException($"Service of type {type.Name} is not registered. Call Register<{type.Name}>() first.");
            }
            return (T)service;
        }

        /// <summary>
        /// Attempts to get a registered service
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <param name="service">Output parameter for the service instance</param>
        /// <returns>True if service was found, false otherwise</returns>
        public static bool TryGet<T>(out T service) {
            var type = typeof(T);
            if (_services.TryGetValue(type, out var obj) && obj is T typedService) {
                service = typedService;
                return true;
            }
            service = default(T);
            return false;
        }
    }
}
