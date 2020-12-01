using System;

namespace ShortBus
{
    public abstract class RequestInterceptAttribute : Attribute
    {
        public abstract Type[] GetInterceptors();
    }
}