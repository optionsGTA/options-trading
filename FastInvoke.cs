using System;
using System.Linq.Expressions;
using System.Reflection;

namespace OptionBot
{
    /// <summary>
    /// http://flurfunk.sdx-ag.de/2012/05/c-performance-bei-der-befullungmapping.html
    /// </summary>
    public static class FastInvoke
    {
        static readonly Logger _log = new Logger();

        public static Func<T, TReturn> BuildTypedGetter<T, TReturn>(PropertyInfo propertyInfo)
        {
            var reflGet = (Func<T, TReturn>) Delegate.CreateDelegate(typeof(Func<T, TReturn>), propertyInfo.GetGetMethod());
            return reflGet;
        }

        public static Action<T, TProperty> BuildTypedSetter<T, TProperty>(PropertyInfo propertyInfo)
        {
            var reflSet = (Action<T, TProperty>)Delegate.CreateDelegate(typeof(Action<T, TProperty>), propertyInfo.GetSetMethod());
            return reflSet;
        }

        public static Action<T, object> BuildUntypedSetter<T>(PropertyInfo propertyInfo)
        {
            var targetType = propertyInfo.DeclaringType;
            var methodInfo = propertyInfo.GetSetMethod();
            var exTarget = Expression.Parameter(targetType, "t");
            var exValue = Expression.Parameter(typeof(object), "p");
            // wir betreiben ein anObject.SetPropertyValue(object)
            var exBody = Expression.Call(exTarget, methodInfo, Expression.Convert(exValue, propertyInfo.PropertyType));
            var lambda = Expression.Lambda<Action<T, object>>(exBody, exTarget, exValue);
            // (t, p) => t.set_StringValue(Convert(p))

            var action = lambda.Compile();
            return action;
        }

        public static Func<T, object> BuildUntypedGetter<T>(PropertyInfo propertyInfo)
        {
            var targetType = propertyInfo.DeclaringType;
            var methodInfo = propertyInfo.GetGetMethod();
            var returnType = methodInfo.ReturnType;

            var exTarget = Expression.Parameter(targetType, "t");
            var exBody = Expression.Call(exTarget, methodInfo);
            var exBody2 = Expression.Convert(exBody, typeof(object));

            var lambda = Expression.Lambda<Func<T, object>>(exBody2, exTarget);
            // t => Convert(t.get_Foo())

            var action = lambda.Compile();
            return action;
        }

        public static Func<object, object> BuildUntypedGetterByObject<T>(PropertyInfo propertyInfo)
        {
            try {
                var targetType = propertyInfo.DeclaringType;
                var methodInfo = propertyInfo.GetGetMethod();
                var returnType = methodInfo.ReturnType;

                var param = Expression.Parameter(typeof(object), "o");
                var exTarget = Expression.Convert(param, typeof(T));
                var exBody = Expression.Call(exTarget, methodInfo);
                var exBody2 = Expression.Convert(exBody, typeof(object));

                var lambda = Expression.Lambda<Func<object, object>>(exBody2, param);
                // o => Convert(((T)o).get_Foo())

                Func<object, object> result = lambda.Compile();

                return result;
            } catch(Exception e) {
                _log.Dbg.AddWarningLog("Unable to compile lambda for {0}.{1}: {2}", typeof(T).Name, propertyInfo.Name, e);
                return null;
            }
        }

        public static LambdaExpression BuildTypedGetterExpressionByPropertyName<T>(this string name) {
            try {
                Expression expr;
                ParameterExpression param;
                var type = typeof(T);
                var parts = name.Split('.');

                expr = param = Expression.Parameter(type, "o");

                foreach(var part in parts) {
                    var info = type.GetProperty(part);
                    expr = Expression.Property(expr, info.Name);
                    type = info.PropertyType;
                }

                expr = Expression.Convert(expr, type);

                var lambda = Expression.Lambda(expr, param);
                return lambda;
            } catch(Exception e) {
                _log.Dbg.AddErrorLog("unable to create expression {0}.{1}: {2}", typeof(T).Name, name, e);
                return null;
            }
        }

        public static Delegate BuildTypedGetterByPropertyName<T>(this string name) {
            try {
                var lambda = name.BuildTypedGetterExpressionByPropertyName<T>();
                var action = lambda.Compile();

                return action;
            } catch(Exception e) {
                _log.Dbg.AddErrorLog("unable to create getter {0}.{1}: {2}", typeof(T).Name, name, e);
                return null;
            }
        }
    }
}
