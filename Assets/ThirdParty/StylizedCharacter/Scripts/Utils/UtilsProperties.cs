using System;
using System.Linq.Expressions;

namespace NHance.Assets.Scripts.Utils
{
    public static class UtilsProperties
    {
        public static string GetMemberName<T, TValue>(Expression<Func<T, TValue>> memberAccess)
        {
            return ((MemberExpression)memberAccess.Body).Member.Name;
        }
    }
}