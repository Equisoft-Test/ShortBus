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
            var response = new Response<TResponseData>();

            try
            {
                var plan = new MediatorPlan<TResponseData>(typeof (IRequestHandler<,>), "Handle", request.GetType(), _dependencyResolver);

                response.Data = plan.Invoke(request);
            }
            catch (Exception e)
            {
                response.Exception = e;
            }

            return response;
        }

        public async Task<Response<TResponseData>> RequestAsync<TResponseData>(IAsyncRequest<TResponseData> query)
        {
            Response<TResponseData> response = new Response<TResponseData>();
            try
            {
                var plan = new MediatorPlan<TResponseData>(typeof(IAsyncRequestHandler<,>), "HandleAsync", query.GetType(), this._dependencyResolver);
                var implementationMethod = plan.HandlerInstance.GetType().GetMethod(plan.HandleMethod.Name, ((IEnumerable<ParameterInfo>)plan.HandleMethod.GetParameters()).Select(info => info.ParameterType).ToArray());
                var interceptors = implementationMethod.GetCustomAttributes()
                    .Where(attribute => attribute is RequestInterceptAttribute)
                    .Cast<RequestInterceptAttribute>()
                    .SelectMany(attribute => attribute.GetInterceptors())
                    .Select(type => _dependencyResolver.GetInstance(type))
                    .Cast<RequestInterceptor>()
                    .ToList();
                
                foreach (RequestInterceptor requestInterceptor in interceptors)
                    requestInterceptor.BeforeInvoke(plan.HandleMethod, query, query.GetType());

                response.Data = await plan.InvokeAsync(query);

                foreach (RequestInterceptor requestInterceptor in interceptors)
                    requestInterceptor.AfterInvoke(plan.HandleMethod, query, query.GetType(), response.Data);
            }
            catch (Exception ex)
            {
                response.Exception = ex;
            }

            return response;
        }

        public Response Notify<TNotification>(TNotification notification)
        {
            IEnumerable<INotificationHandler<TNotification>> instances = this._dependencyResolver.GetInstances<INotificationHandler<TNotification>>();
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