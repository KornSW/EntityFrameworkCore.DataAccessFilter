using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using Security;
//using Microsoft.EntityFrameworkCore;

namespace System.Data.AccessControl {

  public static class AccessControlExtensions {

    public static IQueryable<TEntity> AccessScopeFiltered<TEntity>(this IQueryable<TEntity> extendee) {
      var filterExpression = EntityAccessControl.BuildExpressionIncludingPrincipals<TEntity>(AccessControlContext.Current);
      return extendee.Where(filterExpression);
    }

  }

}
