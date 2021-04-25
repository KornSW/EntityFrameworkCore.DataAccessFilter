using System;
using System.Collections.Generic;
using System.Reflection;

namespace System.Data.AccessControl {

  /// <summary> indicates, that the value of this property represents a classification
  /// within the specified access-control-dimension for which explicit clearances
  /// are requied when working with this entity</summary>
  [AttributeUsage(AttributeTargets.Property)]
  public class AccessControlClassificationAttribute : Attribute {

    public AccessControlClassificationAttribute(string dimensionName) {
      this.DimensionName = dimensionName;
    }

    public String DimensionName { get; }

  }

  public interface IClearanceSource {
    DateTime LastClearanceChangeDateUtc { get; }
    String[] GetClearancesOfDimension(string dimensionName);
  }

}
