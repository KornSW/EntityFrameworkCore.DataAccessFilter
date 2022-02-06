using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Security;

namespace System.Data.AccessControl {

  [TestClass]
  public class AccessControlTests {

    [TestMethod]
    public void FilteringExtensionMethodShouldWork() {

      var rootEntities = new List<MockRootEntity>();
      var subEntities = new List<MockSubEntity>();
      var root1 = new MockRootEntity { RootName = "Root1" ,Scope = "A"};
      var root2 = new MockRootEntity { RootName = "Root2", Scope = "B" };
      var root3 = new MockRootEntity { RootName = "Root3", Scope = "A" };
      var child1 = new MockSubEntity { SubName = "Child1" };
      var child2 = new MockSubEntity { SubName = "Child2" };
      var child3 = new MockSubEntity { SubName = "Child3" };
      rootEntities.Add(root1);
      rootEntities.Add(root2);
      rootEntities.Add(root3);
      subEntities.Add(child1);
      subEntities.Add(child2);
      subEntities.Add(child3);
      root1.Childs.Add(child1);
      root2.Childs.Add(child2);
      root3.Childs.Add(child3);
      child1.Parent = root1;
      child2.Parent = root2;
      child3.Parent = root3;

      EntityAccessControl.RegisterPropertyAsAccessControlClassification(
        (MockRootEntity e) => e.Scope, "AccessControlDimension1"
      );

      MockRootEntity[] filteredRootResult;
      MockSubEntity[] filteredResult;

      filteredResult = subEntities.AsQueryable().AccessScopeFiltered().ToArray();
      Assert.AreEqual(0, filteredResult.Length);

      filteredRootResult = rootEntities.AsQueryable().AccessScopeFiltered().ToArray();
      Assert.AreEqual(0, filteredRootResult.Length);

      AccessControlContext.Current.AddClearance("AccessControlDimension1", "A");
      filteredResult = subEntities.AsQueryable().AccessScopeFiltered().ToArray();
      Assert.AreEqual(2, filteredResult.Length);

      filteredRootResult = rootEntities.AsQueryable().AccessScopeFiltered().ToArray();
      Assert.AreEqual(2, filteredRootResult.Length);

      AccessControlContext.Current.AddClearance("AccessControlDimension1", "B");
      filteredResult = subEntities.AsQueryable().AccessScopeFiltered().ToArray();
      Assert.AreEqual(3, filteredResult.Length);

      filteredRootResult = rootEntities.AsQueryable().AccessScopeFiltered().ToArray();
      Assert.AreEqual(3, filteredRootResult.Length);

    }

  }
  internal class MockRootEntity {

    [Dependent]
    public List<MockSubEntity> Childs { get; set; } = new List<MockSubEntity>();
    public String RootName { get; set; } = null;
    public String Scope { get; set; } = null;

  }

  internal class MockSubEntity {

    [Principal]
    public MockRootEntity Parent { get; set; } = null;

    public String SubName { get; set; } = null;

  }

}
