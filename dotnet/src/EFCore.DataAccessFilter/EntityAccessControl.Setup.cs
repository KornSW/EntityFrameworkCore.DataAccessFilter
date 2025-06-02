using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace System.Data.AccessControl {

  partial class EntityAccessControl {

    //WIRE UP THE CLEARANCE SOURCE
    public delegate string[] ClearancesGetterMethod(string dimensionName);
    public static ClearancesGetterMethod ClearanceGetter { get; set; } = null;
    internal static string[] GetCurrentClearances(string dimensionName) {

      if (ClearanceGetter == null) {
        throw new Exception($"{nameof(EntityAccessControl)}.{nameof(ClearanceGetter)} was not set! (this must be done during startup)");
      }
      return ClearanceGetter.Invoke(dimensionName);
    }

    //STATIC!!!!
    public static void RegisterPropertyAsAccessControlClassification<TEntity>(Expression<Func<TEntity, object>> propertyExpression, string dimensionName) {
      Type entityType = typeof(TEntity);
      PropertyInfo prop = null;

      if (propertyExpression.Body is MemberExpression) {
        var mex = propertyExpression.Body as MemberExpression;
        prop = (mex.Member as PropertyInfo);
      }
      else {
        var uex = propertyExpression.Body as UnaryExpression;
        if (uex.Operand is MemberExpression) {
          var mex = uex.Operand as MemberExpression;
          prop = (mex.Member as PropertyInfo);
        }
      }

      if(prop == null) {
        throw new ArgumentException("Invalid expression");
      }

      GetBuilderForEntity(entityType).RegisterPropertyAsAccessControlClassification(prop, dimensionName);
    }

    //INSTANCE - MEMBER!!!!
    public void RegisterPropertyAsAccessControlClassification(PropertyInfo propertyInfo, string dimensionName) {
      lock (this) {

        if (propertyInfo.DeclaringType != this.EntityType) {
          throw new Exception("this property is not a property of " + this.EntityType.Name);
        }

        if (!_SupportedPropTypes.Contains(propertyInfo.PropertyType)) {
          throw new Exception($"Only properties with the following types can be used as AccessControlClassification: " + String.Join(", ", _SupportedPropTypes.Select((t) => t.Name).ToArray()));
        }

        _AcDimensionsPerPropertyName[propertyInfo] = dimensionName;

        //invalidate - force a recreate of the expression
        _LocalFilterExpression = null;
      }
    }

    private String[] RelatedDimensionNames {
      get {
        lock (_AcDimensionsPerPropertyName) {
          return _AcDimensionsPerPropertyName.Select((kvp) => kvp.Value).Distinct().ToArray();
        }
      }
    }

  }

}
