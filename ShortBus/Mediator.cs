namespace ShortBus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;

    public interface IMediator
    {
        Response<TResponseData> Request<TResponseData>(IRequest<TResponseData> request);
        Task<Response<TResponseData>> RequestAsync<TResponseData>(IAsyncRequest<TResponseData> query);

        Response Notify<TNotification>(TNotification notification);
        Task<Response> NotifyAsync<TNotification>(TNotification notification);
    }

    public class Mediator : IMediator
    {
        readonly IDependencyResolver _dependencyResolver;

        public Mediator(IDependencyResolver dependencyResolver)
        {
            _dependencyResolver = dependencyResolver;
        }

        public virtual Response<TResponseData> Request<TResponseData>(IRequest<TResponseData> request)
        {
            Response<TResponseData> response = new Response<TResponseData>();
            try
            {
                var plan = new MediatorPlan<TResponseData>(typeof(IRequestHandler<,>), "Handle", request.GetType(), _dependencyResolver);
                List<RequestInterceptor> requestInterceptors = GetRequestInterceptors(plan);

                foreach (RequestInterceptor requestInterceptor in requestInterceptors)
                    requestInterceptor.BeforeInvoke(plan.HandleMethod, request, request.GetType());
                response.Data = plan.Invoke(request);

                foreach (RequestInterceptor requestInterceptor in requestInterceptors)
                    requestInterceptor.AfterInvoke(plan.HandleMethod, request, request.GetType(), response.Data);
            }
            catch (Exception ex)
            {
                response.Exception = ex;
            }
            return response;
        }

        private List<RequestInterceptor> GetRequestInterceptors<TResponseData>(MediatorPlan<TResponseData> plan)
        {
            MethodInfo method = plan.HandlerInstance.GetType().GetMethod(plan.HandleMethod.Name, ((IEnumerable<ParameterInfo>)plan.HandleMethod.GetParameters()).Select(info => info.ParameterType).ToArray());
            List<RequestInterceptor> requestInterceptorList = new List<RequestInterceptor>();
            foreach (Attribute customAttribute in method.GetCustomAttributes())
            {
                if (customAttribute is RequestInterceptAttribute)
                {
                    foreach (Type interceptor in ((RequestInterceptAttribute)customAttribute).GetInterceptors())
                    {
                        RequestInterceptor instance = (RequestInterceptor)_dependencyResolver.GetInstance(interceptor);
                        instance.RequestInterceptAttribute = (RequestInterceptAttribute)customAttribute;
                        requestInterceptorList.Add(instance);
                    }
                }
            }
            return requestInterceptorList;
        }

        public async Task<Response<TResponseData>> RequestAsync<TResponseData>(IAsyncRequest<TResponseData> query)
        {
            Response<TResponseData> response = new Response<TResponseData>();
            try
            {
                var plan = new MediatorPlan<TResponseData>(typeof(IAsyncRequestHandler<,>), "HandleAsync", query.GetType(), _dependencyResolver);
                var interceptors = GetRequestInterceptors(plan);

                foreach (var requestInterceptor in interceptors)
                    await requestInterceptor.BeforeInvokeAsync(plan.HandleMethod, query, query.GetType());

                response.Data = await plan.InvokeAsync((object)query);

                foreach (var requestInterceptor in interceptors)
                    await requestInterceptor.AfterInvokeAsync(plan.HandleMethod, query, query.GetType(), response.Data);
            }
            catch (Exception ex)
            {
                response.Exception = ex;
            }
            return response;
        }

        public Response Notify<TNotification>(TNotification notification)
        {
            IEnumerable<INotificationHandler<TNotification>> instances = _dependencyResolver.GetInstances<INotificationHandler<TNotification>>();
            Response response = new Response();
            List<Exception> exceptionList = null;
            foreach (INotificationHandler<TNotification> notificationHandler in instances)
            {
                try
                {
                    notificationHandler.Handle(notification);
                }
                catch (Exception ex)
                {
                    (exceptionList ?? (exceptionList = new List<Exception>())).Add(ex);
                }
            }
            if (exceptionList != null)
                response.Exception = new AggregateException(exceptionList);
            return response;
        }

        public async Task<Response> NotifyAsync<TNotification>(TNotification notification)
        {
            var handlers = _dependencyResolver.GetInstances<IAsyncNotificationHandler<TNotification>>();

            return await Task
                .WhenAll(handlers.Select(x => notifyAsync(x, notification)))
                .ContinueWith(task =>
                {
                    var exceptions = task.Result.Where(exception => exception != null).ToArray();
                    var response = new Response();

                    if (exceptions.Any())
                    {
                        response.Exception = new AggregateException(exceptions);
                    }

                    return response;
                });
        }

        static async Task<Exception> notifyAsync<TNotification>(IAsyncNotificationHandler<TNotification> asyncCommandHandler, TNotification message)
        {
            try
            {
                await asyncCommandHandler.HandleAsync(message);
            }
            catch (Exception ex)
            {
                return ex;
            }

            return null;
        }

        class MediatorPlan<TResult>
        {
            public MethodInfo HandleMethod;
            public Func<object> HandlerInstanceBuilder;
            public object HandlerInstance;

            public MediatorPlan(Type handlerTypeTemplate, string handlerMethodName, Type messageType, IDependencyResolver dependencyResolver)
            {
                var handlerType = handlerTypeTemplate.MakeGenericType(messageType, typeof(TResult));
                HandleMethod = GetHandlerMethod(handlerType, handlerMethodName, messageType);
                HandlerInstanceBuilder = () => dependencyResolver.GetInstance(handlerType);
                HandlerInstance = HandlerInstanceBuilder();
            }

            MethodInfo GetHandlerMethod(Type handlerType, string handlerMethodName, Type messageType)
            {
                return handlerType
                    .GetMethod(handlerMethodName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod,
                        null, CallingConventions.HasThis,
                        new[] { messageType },
                        null);
            }

            public TResult Invoke(object message)
            {
                return (TResult) HandleMethod.Invoke(HandlerInstance, new[] { message });
            }

            public async Task<TResult> InvokeAsync(object message)
            {
                return await (Task<TResult>) HandleMethod.Invoke(HandlerInstance, new[] { message });
            }
        }
    }
}