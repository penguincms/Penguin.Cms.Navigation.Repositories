using Penguin.Cms.Repositories;
using Penguin.Extensions.Collections;
using Penguin.Messaging.Core;
using Penguin.Messaging.Persistence.Messages;
using Penguin.Navigation.Abstractions.Extensions;
using Penguin.Persistence.Abstractions.Interfaces;
using Penguin.Security.Abstractions.Extensions;
using Penguin.Security.Abstractions.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Penguin.Cms.Navigation.Repositories
{
    public class NavigationMenuRepository : AuditableEntityRepository<NavigationMenuItem>

    {
        private Func<NavigationMenuItem, bool> Filter => (entity) =>
         {
             return this.SecurityProvider.TryCheckAccess(entity);
         };

        private ISecurityProvider<NavigationMenuItem> SecurityProvider { get; set; }

        public NavigationMenuRepository(IPersistenceContext<NavigationMenuItem> dbContext, ISecurityProvider<NavigationMenuItem> securityProvider = null, MessageBus messageBus = null) : base(dbContext, messageBus)
        {
            this.SecurityProvider = securityProvider;
        }

        public override void AcceptMessage(Updating<NavigationMenuItem> update)
        {
            if (update is null)
            {
                throw new System.ArgumentNullException(nameof(update));
            }

            update.Target.UpdateProperties();

            base.AcceptMessage(update);
        }

        /// <summary>
        /// Adds a child to the parent. This exists because attempting to load and add the child can potentially
        /// Orphan navigation menu items that the user doesn't have permission to see, since they wont be a part
        /// Of the model when its passed back in
        /// </summary>
        /// <param name="ParentUri"></param>
        /// <param name="child"></param>
        [SuppressMessage("Design", "CA1054:Uri parameters should not be strings")]
        public void AddChild(string ParentUri, NavigationMenuItem child)
        {
            NavigationMenuItem Parent = this.Where(n => n.Uri == ParentUri).FirstOrDefault();

            Parent.AddChild(child);

            this.AddOrUpdate(child);
        }

        /// <summary>
        /// Adds a new entity if none with matching URI exists, otherwise merges with the existing menu
        /// </summary>
        /// <param name="entity">The Navigation Menu Item to add or use as a merge source</param>
        public void AddIfNew(NavigationMenuItem entity)
        {
            if (entity is null)
            {
                throw new System.ArgumentNullException(nameof(entity));
            }

            entity.UpdateProperties();

            NavigationMenuItem toUpdate = this.Where(n => n.Uri == entity.Uri).FirstOrDefault();

            if (toUpdate == null)
            {
                this.Add(entity);
            }
            else
            {
                this.Update(toUpdate.Merge(entity));
            }
        }

        /// <summary>
        /// Gets a navigation menu item with a matching href
        /// </summary>
        /// <param name="pathAndQuery">The href to search for</param>
        /// <returns>A matching navigation menu item or null</returns>
        public NavigationMenuItem GetByHref(string pathAndQuery)
        {
            return this.Where(n => n.Href == pathAndQuery).FirstOrDefault();
        }

        /// <summary>
        /// Gets a navigation menu item by name
        /// </summary>
        /// <param name="name">The name of the navigation menu item to get</param>
        /// <returns>A navigation menu item with a matching name, or null</returns>
        public NavigationMenuItem GetByName(string name)
        {
            NavigationMenuItem topLevel = this.Where(n => n.Name == name).FirstOrDefault(this.Filter);

            if (topLevel != null)
            {
                return this.RecursiveFill(topLevel);
            }

            return null;
        }

        /// <summary>
        /// Gets the child list of navigation menu items for the parent with the given Id
        /// </summary>
        /// <param name="parentId">The Id of the navigation menu item to get the children of</param>
        /// <returns>The child list of navigation menu items for the parent with the given Id</returns>
        public List<NavigationMenuItem> GetByParentId(int parentId)
        {
            return this.Where(n => n.Parent != null && n.Parent._Id == parentId).ToList(this.Filter);
        }

        /// <summary>
        /// Gets a navigation menu item by URI
        /// </summary>
        /// <param name="uri">The URI to search for</param>
        /// <returns>A navigation menu item with matching URI, or null</returns>
        [SuppressMessage("Design", "CA1054:Uri parameters should not be strings")]
        public NavigationMenuItem GetByUri(string uri)
        {
            return this.Where(n => n.Uri == uri).FirstOrDefault(this.Filter);
        }

        /// <summary>
        /// Returns a root navigation menu item with a matching name, or null
        /// </summary>
        /// <param name="name">The name of the navigation menu item to get</param>
        /// <returns>A root navigation menu item with a matching name, or null</returns>
        public NavigationMenuItem GetRootByName(string name)
        {
            return this.RecursiveFill(this.Where(n => n.Name == name && n.Parent == null).FirstOrDefault(this.Filter));
        }

        /// <summary>
        /// Gets all root menus
        /// </summary>
        /// <returns>A recursive list of root menus</returns>
        public List<NavigationMenuItem> GetRootMenus()
        {
            return this.Where(n => n.Parent == null).ToList().Where(this.Filter).Select(n => this.RecursiveFill(n)).ToList();
        }

        private NavigationMenuItem RecursiveFill(NavigationMenuItem navigationMenuItem, List<NavigationMenuItem> AllNavigationMenus = null)
        {
            List<NavigationMenuItem> Source = AllNavigationMenus ?? this.All.ToList();

            ILookup<int, NavigationMenuItem> AllItems = Source.ToLookup(k => k.Parent?._Id ?? 0, v => v);

            new List<NavigationMenuItem> { navigationMenuItem }.RecursiveProcess(thisChild =>
            {
                thisChild.Children = AllItems[thisChild._Id].Where(this.Filter).ToList();

                thisChild.Children.OrderBy(n => n.Ordinal);

                return thisChild.Children;
            });

            if (navigationMenuItem != null)
            {
                navigationMenuItem.UpdateProperties();
                return navigationMenuItem;
            }
            else
            {
                throw new ArgumentNullException(nameof(navigationMenuItem));
            }
        }
    }
}