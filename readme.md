# Mandatory Access Control



## Setup

```c#
  static MyDbContext() {

    //MAP PROPERTIES AS 'CLASSIFICATIONS'
    EntityAccessControl.RegisterPropertyAsAccessControlClassification(
      (ArchiveEntity e) => e.TenantName, "Tenant"
    );

    EntityAccessControl.RegisterPropertyAsAccessControlClassification(
      (DocumentEntity e) => e.ConfidentialityLevel, "ConfLevel"
    );

    //USE AMBIENT SCOPES (comming via CurrentSecurityToken) AS 'CLEARANCES'
    EntityAccessControl.ClearanceGetter = (
      (scopeDimension) => {
        if (scopeDimension == "Tenant") return GetPermittedTenantsFromCurrentSecurityToken();
        if (scopeDimension == "ConfLevel") return new string() {4,3,2};
        return new string[] { };
      }
    );

  }
```



## Usage with EF

```c#
using (var db = new MyDbContext()) {

  DocumentEntity[] documentsAllowedToLoad = db.Documents
      .AccessScopeFiltered() //<< THIS COMES FROM US
      .Where((doc)=>doc.Name.Contains("Bar")
      .ToArray();

};
```

**It is also evaluating the clearances for the Tenant-Field, located at the ArchiveEntity (which is the principal of the DocumentEntity), which were accessing!**