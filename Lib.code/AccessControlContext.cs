using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace System.Data.AccessControl {

  public class AccessControlContext : IDisposable, IClearanceSource {
     
    #region ' static part (Ambience) '

    private static Dictionary<string, AccessControlContext> _Instances = new Dictionary<string, AccessControlContext>();

    private static AsyncLocal<string[]> _CurrentIdPath = new AsyncLocal<string[]>();

    private static AccessControlContext _DefaultInstance = new AccessControlContext(true);

    public static AccessControlContext Current {
      get {
        string[] currentPath = _CurrentIdPath.Value;
        if (currentPath == null || currentPath.Length < 1) {
          return _DefaultInstance;
        }
        AccessControlContext currentInstance;
        lock (_Instances) {
          currentInstance = _Instances[string.Join("/", currentPath)];
        }
        return currentInstance;
      }
    }

    #endregion

    #region ' context handling '

    private String _Id = null;
    private String[] _Path = null;
    private AccessControlContext _Parent = null;
    private bool _IsDefault = false;

    public AccessControlContext() { 
      _Id = Guid.NewGuid().ToString();
      this.PushContextPath();
    }

    private AccessControlContext(bool isDefault) {
      _IsDefault = isDefault;
      _Id = Guid.NewGuid().ToString();
      this.PushContextPath();
    }

    private void PushContextPath() {

      List<String> pth;
      string[] currentPath = _CurrentIdPath.Value;
      if (currentPath == null || currentPath.Length < 1) {
        pth = new List<String>();
        _Parent = null;
      }
      else {
        pth = currentPath.ToList();
        lock (_Instances) {
          _Parent = _Instances[string.Join("/", currentPath)];
        }
      }

      pth.Add(_Id);
      _Path = pth.ToArray();

      var pathString = string.Join("/", _Path);

      _CurrentIdPath.Value = _Path;
      lock (_Instances) {
        _Instances[pathString] = this;
      }

    }

    public String Id {
      get {
        return _Id;
      }
    }

    public  AccessControlContext Parent {
      get {
        return _Parent;
      }
    }
    public void Dispose() {

      if (_IsDefault) {
        //the default instance will never pop the context
        return;
      }

      if (this.Parent != null) {
        _CurrentIdPath.Value = this.Parent._Path;
      }

      var pathString = string.Join("/", _Path);
      lock (_Instances) {
        _Instances.Remove(pathString);
      }

    }

    #endregion

    public void SetAccessorNaame(string newAccessorNaame) {
      _AccessorName = newAccessorNaame;
    }

    private String _AccessorName = null;
    public String AccessorName {
      get {
        if (_AccessorName == null && _Parent != null) {
          return _Parent.AccessorName;
        }
        return _AccessorName;
      }
    }

    #region ' CLEARANCES '

    private DateTime _LastClearanceChangeDateUtc = DateTime.UtcNow;

    DateTime IClearanceSource.LastClearanceChangeDateUtc {
      get {
        return _LastClearanceChangeDateUtc;
      }
    }

    Dictionary<String, List<String>> _ClearancesPerDimension = new Dictionary<String, List<String>>();

    string[] IClearanceSource.GetClearancesOfDimension(string dimensionName) {
      lock (_ClearancesPerDimension) {
        if (!_ClearancesPerDimension.ContainsKey(dimensionName)) {
          return new string[] { };
        }
        return _ClearancesPerDimension[dimensionName].ToArray();
      }
    }

    public void AddClearance(string dimensionName, string targetClassificationValue) {
      lock (_ClearancesPerDimension) {
        List<String> lst;
        if (_ClearancesPerDimension.ContainsKey(dimensionName)) {
          lst = _ClearancesPerDimension[dimensionName];
          if (!lst.Contains(targetClassificationValue)) {
            lst.Add(targetClassificationValue);
          }
        }
        else {
          lst = new List<String>();
          lst.Add(targetClassificationValue);
          _ClearancesPerDimension.Add(dimensionName, lst);
        }
      }
    }
    public void AddClearances(string dimensionName, params string[] targetClassificationValues) {
      lock (_ClearancesPerDimension) {
        List<String> lst;
        if (_ClearancesPerDimension.ContainsKey(dimensionName)) {
          lst = _ClearancesPerDimension[dimensionName];
          foreach (string targetClassificationValue in targetClassificationValues) {
            if (!lst.Contains(targetClassificationValue)) {
              lst.Add(targetClassificationValue);
            }
          }
        }
        else {
          _ClearancesPerDimension.Add(dimensionName, targetClassificationValues.ToList());
        }
      }
    }

    /// <summary>
    /// supports config objects with properties like: .DimensionName="CleanranceA,ClearanceB"
    /// </summary>
    public void AddClearances(object anonymousObject) {

      Type i = anonymousObject.GetType();
      foreach (PropertyInfo ip in i.GetProperties()) {
        string dimensionName = ip.Name;
        string[] targetClassifications;

        object targetClassificationsUntyped = ip.GetValue(anonymousObject);

        if(targetClassificationsUntyped != null) {
           targetClassifications = (
            targetClassificationsUntyped
            .ToString()
            .Split(',')
            .Select((c)=>c.Trim())
            .Where((c)=> !String.IsNullOrWhiteSpace(c)
           ).ToArray());
        }
        else {
          targetClassifications = new string[] { };
        }

        this.AddClearances(dimensionName, targetClassifications);
      }

    }

    #endregion

    #region ' PERMISSIONS '

    private List<string> _GrandedPermissions = new List<string>();
    public void AddPermissions(params string[] permissionsToAppend) {
      if (permissionsToAppend == null) {
        return;
      }
      lock (_GrandedPermissions) {
        foreach (string p in permissionsToAppend) {
          if(p.StartsWith("!")) {
            _DeniedPermissions.Add(p.Substring(1));
          }
          else {
            _GrandedPermissions.Add(p);
          }
        }
      }
    }

    private List<string> _DeniedPermissions = new List<string>();
    public void AddDeniedPermissions(params string[] permissionsToAppend) {
      if (permissionsToAppend == null) {
        return;
      }
      lock (_DeniedPermissions) {
        foreach (string p in permissionsToAppend) {
          _DeniedPermissions.Add(p);
        }
      }
    }

    public String[] GrandedPermissions {
      get {
        if(_Parent == null) {
          lock (_GrandedPermissions) {
            return _GrandedPermissions.ToArray();
          }
        }
        else {
          lock (_GrandedPermissions) {
            return _GrandedPermissions.Union(_Parent.GrandedPermissions).ToArray();
          }
        }
      }
    }

    public String[] DeniedPermissions {
      get {
        if (_Parent == null) {
          lock (_DeniedPermissions) {
            return _DeniedPermissions.ToArray();
          }
        }
        else {
          lock (_DeniedPermissions) {
            return _DeniedPermissions.Union(_Parent.DeniedPermissions).ToArray();
          }
        }
      }
    }

    public String[] EffectivePermissions {
      get {
        return this.GrandedPermissions.Except(this.DeniedPermissions).ToArray();
      }
    }

    public bool HasEffectivePermission(string permission) {
      bool result = false;
      lock (_GrandedPermissions) {
        foreach (string gp in _GrandedPermissions) {
          if(Regex.IsMatch(permission, gp)){
            result = true;
            break;
          }
        }
      }
      lock (_GrandedPermissions) {
        foreach (string dp in _DeniedPermissions) {
          if (Regex.IsMatch(permission, dp)){
            result = false;
            break;
          }
        }
      }

      return result;
    }

    //public AccessSpecs GetAccessSpecs(string typeName) {
    //  string entityName = typeName.ToLower();
    //  string[] grandedPermissions = this.GrandedPermissions;
    //  string[] deniedPermissions = this.DeniedPermissions;
    //  AccessSpecs acc = new AccessSpecs();

    //  acc.CanRead = this.HasEffectivePermission("read-" + entityName);
    //  acc.CanAddNew = this.HasEffectivePermission("add-" + entityName);
    //  acc.CanUpdate = this.HasEffectivePermission("update-" + entityName);
    //  acc.CanDelete = this.HasEffectivePermission("delete-" + entityName);

    //  return acc;
    //}

    #endregion

  }

}
