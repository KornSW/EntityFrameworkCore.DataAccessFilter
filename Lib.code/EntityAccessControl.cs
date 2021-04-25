using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace System.Data.AccessControl {

  public class EntityAccessControl {

    private ParameterExpression _EntityParameter;
    private Dictionary<PropertyInfo, String> _AcDimensionsPerPropertyName = new Dictionary<PropertyInfo, String>();
    private Dictionary<PropertyInfo, Type> _UpNavigations;

    public Type EntityType { get; }

    private EntityAccessControl(Type entityType) {
      this.EntityType = entityType;
      _EntityParameter = Expression.Parameter(entityType, entityType.Name);

      foreach (PropertyInfo prop in entityType.GetProperties()) {
        foreach (Attribute attr in prop.GetCustomAttributes()) {
          if (attr.GetType() == typeof(AccessControlClassificationAttribute)) {
            this.RegisterPropertyAsAccessControlClassification(prop, (attr as AccessControlClassificationAttribute).DimensionName);
          }
        }
      }

      _UpNavigations = entityType.GetNavigations(
        includePrincipals: true,
        includeLookups: true,
        includeDependents: false,
        includeReferers: false
      );

    }

    public void RegisterPropertyAsAccessControlClassification(PropertyInfo propertyInfo, string dimensionName) {
      lock (this) {

        if (propertyInfo.DeclaringType != this.EntityType) {
          throw new Exception("this property is not a property of " + this.EntityType.Name);
        }

        if (!_SupportedPropTypes.Contains(propertyInfo.PropertyType)) {
          throw new Exception($"Only properties with the following types can be used as AccessControlClassification: " + String.Join(", ", _SupportedPropTypes.Select((t)=> t.Name).ToArray()));
        }

        _AcDimensionsPerPropertyName[propertyInfo] = dimensionName;

        //invalidate - force a recreate of the expression
        _LocalFilterExpression = null;
      }
    }

    //CACHE
    private Expression _LocalFilterExpression = null;
    private DateTime _ClearanceVersionWithinFilterExpression = DateTime.MinValue;
    private IClearanceSource _LastClearanceSource;

    private bool CheckLocalReBuildRequired(IClearanceSource clearanceSource) {
      if(!ReferenceEquals (_LastClearanceSource, clearanceSource)) {
        _LocalFilterExpression = null;
        _LastClearanceSource = clearanceSource;
      }
      var ccDate = _LastClearanceSource.LastClearanceChangeDateUtc;
      if (ccDate > _ClearanceVersionWithinFilterExpression) {
        _LocalFilterExpression = null;
        _ClearanceVersionWithinFilterExpression = ccDate;
      }
      if(_LocalFilterExpression != null) {
        foreach (Type relatedType in _UpNavigations.Values) {
          if (EntityAccessControl.GetBuilderForEntity(relatedType).CheckLocalReBuildRequired(clearanceSource)) {
            _LocalFilterExpression = null;
            return true;
          }  
        }
      }
      return (_LocalFilterExpression == null);
    }

    /// <summary>
    /// returns null when nothing to validate
    /// </summary>
    public Expression BuildUntypedLocalFilterExpression(IClearanceSource clearanceSource, Expression entitySourceExpression = null) {
      lock (this) {

        if(entitySourceExpression == null) {
          //in default we can use the lamba-entry parameter (which is our entity)
          entitySourceExpression = _EntityParameter;
          //we get external expressions, when we are called to build expression for just a part of the graph
          //in this case a member-expression which navigates from ou child up to us is passed via this arg...
        }

        if(!this.CheckLocalReBuildRequired(clearanceSource)) {
          return _LocalFilterExpression;
        }

        //build expressions for local properties
        foreach (var kvp in _AcDimensionsPerPropertyName) {
          var propName = kvp.Key.Name;
          var dimensionName = kvp.Value;
          var propExpr = Expression.Property(entitySourceExpression, propName);
          string[] dimensionClearanceValues = clearanceSource.GetClearancesOfDimension(dimensionName);
          var localExpression = this.BuildAcValueOrExpressionForDimensionClearanceValues(propExpr, kvp.Key.PropertyType, dimensionClearanceValues);
          if (_LocalFilterExpression == null) {
            _LocalFilterExpression = localExpression;
          }
          else {
            _LocalFilterExpression = Expression.AndAlso(_LocalFilterExpression, localExpression);
          }
        }

        //can be null!!!
        return _LocalFilterExpression;
      }
    }   

    private Expression BuildAcValueOrExpressionForDimensionClearanceValues(MemberExpression propExpr,Type propertyType, string[] dimensionClearanceValues) {

      if (!dimensionClearanceValues.Any()) {
        return BinaryExpression.Constant(false);
      }

      Expression grandExpr = null;
      Expression denyExpr = null;

      foreach (string dimensionClearanceValue in dimensionClearanceValues) {
        string value = dimensionClearanceValue.Trim();
        Expression constClearance = this.BuildConstValueExpressionFromString(propertyType, value);
        if (value.StartsWith("!")) {
          value = value.Substring(1);
          if(value == "*") {
            return BinaryExpression.Constant(false);
          }
          if(denyExpr == null) {
            denyExpr = Expression.Equal(propExpr, constClearance);
          }
          else {
            denyExpr = Expression.OrElse(denyExpr, Expression.Equal(propExpr, constClearance));
          }
        }
        else {
          if (value == "*") {
            return BinaryExpression.Constant(true);
          }
          if (grandExpr == null) {
            grandExpr = Expression.Equal(propExpr, constClearance);
          }
          else {
            grandExpr = Expression.OrElse(grandExpr, Expression.Equal(propExpr, constClearance));
          }
        }
      }

      if (denyExpr == null) {
        if (grandExpr == null) {
          return BinaryExpression.Constant(false);
        }
        else {
          return grandExpr;
        }
      }
      else {
        Expression notDeniedExpr = Expression.Not(denyExpr);
        if (grandExpr == null) {
          return notDeniedExpr;
        }
        else {
          return Expression.AndAlso (grandExpr, notDeniedExpr);
        }
      }
    }

    private Expression BuildConstValueExpressionFromString(Type targetType, object value) {

      if (value == null) {
        return Expression.Constant(null, targetType);
      }

      if (targetType == typeof(string)) {
        return Expression.Constant(value.ToString());
      }
      else {

        if (value.GetType() != typeof(string)) {
          if (!targetType.IsAssignableFrom(value.GetType())) {
            return Expression.Constant(value, targetType);
          }
          else {
            throw new InvalidCastException($"Cannot cast from '{value.GetType().Name}' to {targetType.Name}!");
          }
        }

        object castedValue = null;

        if (targetType == typeof(byte))
          castedValue = byte.Parse(value as string);
        if (targetType == typeof(Int16))
          castedValue = Int16.Parse(value as string);
        if (targetType == typeof(Int32))
          castedValue = Int32.Parse(value as string);
        if (targetType == typeof(Int64))
          castedValue = Int64.Parse(value as string);
        if (targetType == typeof(bool))
          castedValue = bool.Parse(value as string);
        if (targetType == typeof(DateTime))
          castedValue = DateTime.Parse(value as string);
        if (targetType == typeof(double))
          castedValue = double.Parse(value as string);
        if (targetType == typeof(decimal))
          castedValue = decimal.Parse(value as string);
        if (targetType == typeof(Guid))
          castedValue = Guid.Parse(value as string);

       return Expression.Constant(castedValue, targetType);
      }

    }

    /// <summary>
    /// returns null when nothing to validate
    /// </summary>
    public Expression BuildUntypedFilterExpressionIncludingPrincipals(IClearanceSource clearanceSource, Expression entitySourceExpression = null) {

      if (entitySourceExpression == null) {
        //in default we can use the lamba-entry parameter (which is our entity)
        entitySourceExpression = _EntityParameter;
        //we get external expressions, when we are called to build expression for just a part of the graph
        //in this case a member-expression which navigates from ou child up to us is passed via this arg...
      }

      Expression result = this.BuildUntypedLocalFilterExpression(clearanceSource, entitySourceExpression);

      foreach (var nav in _UpNavigations) {
        String navPropName = nav.Key.Name;
        Type parentEntityType = nav.Value;
        MemberExpression targetEntityNavigationExpression = MemberExpression.Property(entitySourceExpression, navPropName);
        var builderForParentEntityType = EntityAccessControl.GetBuilderForEntity(parentEntityType);
        Expression targetFilterExpression = builderForParentEntityType.BuildUntypedFilterExpressionIncludingPrincipals(clearanceSource, targetEntityNavigationExpression);
        //only when the nav has any filtering...
        if (targetFilterExpression != null) {
          if (result == null) {
            result = targetFilterExpression;
          }
          else {
            result = Expression.AndAlso(result, targetFilterExpression);
          }
        }
      }

      //can be null!
      return result;
    }

    private Expression<Func<TEntity, bool>> BuildTypedLambdaForLocal<TEntity>(IClearanceSource clearanceSource, Expression entitySourceExpression = null) {
      Expression body = this.BuildUntypedLocalFilterExpression(clearanceSource);
      if( body == null) {
        body = BinaryExpression.Constant(true);
      }
      return Expression.Lambda<Func<TEntity, bool>>(body, _EntityParameter);
    }

    private Expression<Func<TEntity, bool>> BuildTypedLambdaIncludingPrincipals<TEntity>(IClearanceSource clearanceSource, Expression entitySourceExpression = null) {
      Expression body = this.BuildUntypedFilterExpressionIncludingPrincipals(clearanceSource);
      if (body == null) {
        body = BinaryExpression.Constant(true);
      }
      return Expression.Lambda<Func<TEntity, bool>>(body, _EntityParameter);
    }

  #region ' static '

    private static Type[] _SupportedPropTypes = new Type[] { typeof(string), typeof(byte), typeof(Int16), typeof(Int32), typeof(Int64), typeof(bool), typeof(DateTime), typeof(double), typeof(decimal), typeof(Guid) };
    private static MethodInfo _GenericMethodLocal = typeof(EntityAccessControl).GetMethods(BindingFlags.Public | BindingFlags.Static).Where((m) => m.Name == nameof(BuildExpressionForLocalEntity) && m.GetGenericArguments().Any()).Single();
    private static MethodInfo _GenericMethodIncludingPrincipals = typeof(EntityAccessControl).GetMethods(BindingFlags.Public | BindingFlags.Static).Where((m) => m.Name == nameof(BuildExpressionIncludingPrincipals) && m.GetGenericArguments().Any()).Single();
    private static Dictionary<Type, EntityAccessControl> _BuilderInstances = new Dictionary<Type, EntityAccessControl>();

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

    public static EntityAccessControl GetBuilderForEntity<TEntity>() {
      return GetBuilderForEntity(typeof(TEntity));
    }

    public static EntityAccessControl GetBuilderForEntity(Type entityType) {
      lock (_BuilderInstances) {
        if (_BuilderInstances.ContainsKey(entityType)) {
          return _BuilderInstances[entityType];
        }
        var newInstance = new EntityAccessControl(entityType);
        _BuilderInstances[entityType] = newInstance;
        return newInstance;
      }
    }


    public static Expression<Func<TEntity, bool>> BuildExpressionIncludingPrincipals<TEntity>(IClearanceSource clearanceSource) {
      return GetBuilderForEntity(typeof(TEntity)).BuildTypedLambdaIncludingPrincipals<TEntity>(clearanceSource);
    }

    public static Expression BuildExpressionIncludingPrincipals(Type entityType, IClearanceSource clearanceSource) {
      var genmth = _GenericMethodIncludingPrincipals.MakeGenericMethod(entityType);
      var args = new object[] { clearanceSource };
      return genmth.Invoke(null, args) as Expression;
    }

    public static Expression<Func<TEntity, bool>> BuildExpressionForLocalEntity<TEntity>(IClearanceSource clearanceSource) {
      return GetBuilderForEntity(typeof(TEntity)).BuildTypedLambdaForLocal<TEntity>(clearanceSource);
    }

    public static Expression BuildExpressionForLocalEntity(Type entityType, IClearanceSource clearanceSource) {
      var genmth = _GenericMethodLocal.MakeGenericMethod(entityType);
      var args = new object[] { clearanceSource };
      return genmth.Invoke(null, args) as Expression;
    }

  #endregion

  }
}
