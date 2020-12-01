using System;
using System.Reflection;

namespace ShortBus
{
  public abstract class RequestInterceptor
  {
    public abstract void AfterInvoke(
      MethodInfo requestHandler,
      object request,
      Type requestType,
      object responseData);

    public abstract void BeforeInvoke(MethodInfo requestHandler, object request, Type requestType);
  }
}