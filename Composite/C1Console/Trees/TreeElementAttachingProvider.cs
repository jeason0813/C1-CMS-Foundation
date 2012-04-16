﻿using System;
using System.Collections.Generic;
using System.Linq;
using Composite.Data;
using Composite.C1Console.Elements;
using Composite.C1Console.Elements.Plugins.ElementAttachingProvider;
using Composite.Core.Linq;
using Composite.Core.Logging;
using Composite.Core.ResourceSystem;
using Composite.C1Console.Security;
using Composite.C1Console.Trees.Foundation;
using Composite.C1Console.Trees.Foundation.AttachmentPoints;
using Microsoft.Practices.EnterpriseLibrary.Common.Configuration;


namespace Composite.C1Console.Trees
{
    [ConfigurationElementType(typeof(NonConfigurableElementAttachingProvider))]
    internal class TreeElementAttachingProvider : IMultibleResultElementAttachingProvider
    {
        public ElementProviderContext Context
        {
            get;
            set;
        }



        public bool HaveCustomChildElements(EntityToken parentEntityToken, Dictionary<string, string> piggybag)
        {
            TreeSharedRootsFacade.Initialize(Context.ProviderName);

            foreach (Tree tree in TreeFacade.GetTreesByEntityToken(parentEntityToken))
            {
                if (tree.RootTreeNode.ChildNodes.Count() > 0) return true;
            }

            return false;
        }



        public ElementAttachingProviderResult GetAlternateElementList(EntityToken parentEntityToken, Dictionary<string, string> piggybag)
        {
            throw new NotImplementedException("Will never get called");
        }



        public IEnumerable<ElementAttachingProviderResult> GetAlternateElementLists(EntityToken parentEntityToken, Dictionary<string, string> piggybag)
        {
            TreeSharedRootsFacade.Initialize(Context.ProviderName);

            IEnumerable<Tree> trees = TreeFacade.GetTreesByEntityToken(parentEntityToken);

            foreach (Tree tree in trees)
            {
                foreach (IAttachmentPoint attachmentPoint in tree.GetAttachmentPoints(parentEntityToken))
                {
                    TreeNodeDynamicContext dynamicContext = new TreeNodeDynamicContext(TreeNodeDynamicContextDirection.Down)
                    {
                        ElementProviderName = this.Context.ProviderName,
                        Piggybag = piggybag,
                        CurrentEntityToken = parentEntityToken,
                        CurrentTreeNode = tree.RootTreeNode,
                        IsRoot = true
                    };

                    ElementAttachingProviderResult result = null;
                    try
                    {
                        result = new ElementAttachingProviderResult()
                        {
                            Elements = tree.RootTreeNode.GetElements(parentEntityToken, dynamicContext).Evaluate(),
                            Position = attachmentPoint.Position,
                            PositionPriority = 0
                        };
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("TreeFacade", string.Format("Getting elements from the three '{0}' failed", tree.TreeId));
                        LoggingService.LogError("TreeFacade", ex);

                        Element errorElement = ShowErrorElementHelper.CreateErrorElement(
                            StringResourceSystemFacade.GetString("Composite.C1Console.Trees", "KeyFacade.ErrorTreeNode.Label"),
                            tree.TreeId,
                            ex.Message);

                        result = new ElementAttachingProviderResult()
                        {
                            Elements = new List<Element>() { errorElement },
                            Position = attachmentPoint.Position,
                            PositionPriority = 0
                        };
                    }

                    yield return result;
                }
            }

            foreach (CustomTreePerspectiveInfo info in TreeSharedRootsFacade.SharedRootFolders.Values)
            {
                if (info.AttachmentPoint.IsAttachmentPoint(parentEntityToken))
                {
                    ElementAttachingProviderResult result = new ElementAttachingProviderResult
                    {
                        Elements = new [] { info.Element },
                        Position = info.AttachmentPoint.Position,
                        PositionPriority = 10000
                    };

                    yield return result;
                }
            }           
        }



        public IEnumerable<Element> GetChildren(EntityToken parentEntityToken, Dictionary<string, string> piggybag)
        {
            List<Tree> trees;
            if (parentEntityToken is TreePerspectiveEntityToken)
            {
                trees = TreeSharedRootsFacade.SharedRootFolders[parentEntityToken.Id].Trees;
            }
            else
            {
                string treeId = piggybag.Where(f => f.Key == StringConstants.PiggybagTreeId).Single().Value;
                Tree tree = TreeFacade.GetTree(treeId);
                if (tree == null) return new Element[] { };
                trees = new List<Tree> { tree };
            }

            IEnumerable<Element> result = null;

            foreach (Tree tree in trees)
            {
                TreeNodeDynamicContext dynamicContext = new TreeNodeDynamicContext(TreeNodeDynamicContextDirection.Down)
                {
                    ElementProviderName = this.Context.ProviderName,
                    Piggybag = piggybag,
                    CurrentEntityToken = parentEntityToken
                };

                try
                {
                    if (parentEntityToken is TreePerspectiveEntityToken)
                    {
                        TreeNode treeNode = tree.RootTreeNode;

                        dynamicContext.CurrentTreeNode = treeNode;

                        IEnumerable<Element> elements = treeNode.ChildNodes.GetElements(parentEntityToken, dynamicContext);
                        result = result.ConcatOrDefault(elements);
                    }
                    else if (parentEntityToken is TreeSimpleElementEntityToken)
                    {
                        TreeNode treeNode = tree.GetTreeNode(parentEntityToken.Id);
                        if (treeNode == null) throw new InvalidOperationException("Tree is out of sync");

                        dynamicContext.CurrentTreeNode = treeNode;

                        IEnumerable<Element> elements = treeNode.ChildNodes.GetElements(parentEntityToken, dynamicContext);
                        result = result.ConcatOrDefault(elements);
                    }
                    else if (parentEntityToken is TreeFunctionElementGeneratorEntityToken)
                    {
                        TreeNode treeNode = tree.GetTreeNode(parentEntityToken.Id);
                        if (treeNode == null) throw new InvalidOperationException("Tree is out of sync");

                        dynamicContext.CurrentTreeNode = treeNode;

                        IEnumerable<Element> elements = treeNode.ChildNodes.GetElements(parentEntityToken, dynamicContext);
                        result = result.ConcatOrDefault(elements);
                    }
                    else if (parentEntityToken is TreeDataFieldGroupingElementEntityToken)
                    {
                        TreeDataFieldGroupingElementEntityToken castedParentEntityToken = parentEntityToken as TreeDataFieldGroupingElementEntityToken;
                        TreeNode treeNode = tree.GetTreeNode(parentEntityToken.Id);
                        if (treeNode == null) throw new InvalidOperationException("Tree is out of sync");

                        dynamicContext.CurrentTreeNode = treeNode;
                        dynamicContext.FieldGroupingValues = castedParentEntityToken.GroupingValues;
                        dynamicContext.FieldFolderRangeValues = castedParentEntityToken.FolderRangeValues;

                        IEnumerable<Element> elements = treeNode.ChildNodes.GetElements(parentEntityToken, dynamicContext);
                        result = result.ConcatOrDefault(elements);
                    }
                    else if (parentEntityToken is DataEntityToken)
                    {
                        DataEntityToken dataEntityToken = parentEntityToken as DataEntityToken;

                        Type interfaceType = dataEntityToken.InterfaceType;

                        List<TreeNode> treeNodes;
                        if (tree.BuildProcessContext.DataInteraceToTreeNodes.TryGetValue(interfaceType, out treeNodes) == false)
                        {
                            throw new InvalidOperationException();
                        }

                        string parentNodeId = piggybag.GetParentIdFromPiggybag();

                        TreeNode treeNode = treeNodes.Where(f => f.ParentNode.Id == parentNodeId).SingleOrDefault();
                        if (treeNode == null) throw new InvalidOperationException("Tree is out of sync");

                        dynamicContext.CurrentTreeNode = treeNode;

                        IEnumerable<Element> elements = treeNode.ChildNodes.GetElements(parentEntityToken, dynamicContext);
                        result = result.ConcatOrDefault(elements);
                    }
                    else
                    {
                        throw new NotImplementedException("Unhandled entityt token type");
                    }


                    result = result.Evaluate();
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TreeFacade", string.Format("Getting elements from the three '{0}' failed", tree.TreeId));
                    LoggingService.LogError("TreeFacade", ex);

                    Element errorElement = ShowErrorElementHelper.CreateErrorElement(
                        StringResourceSystemFacade.GetString("Composite.C1Console.Trees", "KeyFacade.ErrorTreeNode.Label"),
                        tree.TreeId,
                        ex.Message);

                    return new Element[] { errorElement };
                }
            }

            return result;
        }
    }
}
