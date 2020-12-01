namespace ShortBus.Ninject
{
    using System;
    using System.Collections.Generic;
    using global::Ninject;

    public class NinjectDependencyResolver : IDependencyResolver
    {
        readonly IReadOnlyKernel _container;

        public NinjectDependencyResolver(IReadOnlyKernel container)
        {
            _container = container;
        }

        public object GetInstance(Type type)
        {
            return _container.Get(type);
        }

        public IEnumerable<T> GetInstances<T>()
        {
            return _container.GetAll<T>();
        }
    }
}