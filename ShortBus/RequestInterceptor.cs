using System;
using System.Reflection;
using System.Threading.Tasks;

namespace ShortBus
{
    public abstract class RequestInterceptor
    {
        public RequestInterceptAttribute RequestInterceptAttribute { get; set; }
        
        public abstract void AfterInvoke(
            MethodInfo requestHandler,
            object request,
            Type requestType,
            object responseData);
        
        public abstract void BeforeInvoke(MethodInfo requestHandler, object request, Type requestType);
        
        public abstract Task AfterInvokeAsync(
            MethodInfo requestHandler,
            object request,
            Type requestType,
            object responseData);
        
        public abstract Task BeforeInvokeAsync(
            MethodInfo requestHandler,
            object request,
            Type requestType);
    }
}