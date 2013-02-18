﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Threading;
using MvcSiteMapProvider.Caching;
using MvcSiteMapProvider;
using MvcSiteMapProvider.Builder;
using MvcSiteMapProvider.Web;

namespace MvcSiteMapProvider.Loader
{
    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class SiteMapLoader 
        : ISiteMapLoader
    {
        public SiteMapLoader(
            TimeSpan absoluteCacheExpiration,
            TimeSpan slidingCacheExpiration,
            ISiteMapCache siteMapCache,
            ISiteMapCacheKeyGenerator siteMapCacheKeyGenerator,
            ISiteMapBuilderSetStrategy siteMapBuilderSetStrategy,
            ISiteMapFactory siteMapFactory,
            ISiteMapCacheKeyToBuilderSetMapper siteMapCacheKeyToBuilderSetMapper
            )
        {
            if (siteMapCache == null)
                throw new ArgumentNullException("siteMapCache");
            if (siteMapCacheKeyGenerator == null)
                throw new ArgumentNullException("siteMapCacheKeyGenerator");
            if (siteMapBuilderSetStrategy == null)
                throw new ArgumentNullException("siteMapBuilderSetStrategy");
            if (siteMapFactory == null)
                throw new ArgumentNullException("siteMapFactory");
            if (siteMapCacheKeyToBuilderSetMapper == null)
                throw new ArgumentNullException("siteMapCacheKeyToBuilderSetMapper");

            this.absoluteCacheExpiration = absoluteCacheExpiration;
            this.slidingCacheExpiration = slidingCacheExpiration;
            this.siteMapCache = siteMapCache;
            this.siteMapCacheKeyGenerator = siteMapCacheKeyGenerator;
            this.siteMapBuilderSetStrategy = siteMapBuilderSetStrategy;
            this.siteMapFactory = siteMapFactory;
            this.siteMapCacheKeyToBuilderSetMapper = siteMapCacheKeyToBuilderSetMapper;

            // Attach an event to the cache so when the SiteMap is removed, the Clear() method can be called on it to ensure
            // we don't have any circular references that aren't GC'd.
            siteMapCache.SiteMapRemoved += new EventHandler<SiteMapCacheItemRemovedEventArgs>(siteMapCache_SiteMapRemoved);
        }

        protected readonly TimeSpan absoluteCacheExpiration;
        protected readonly TimeSpan slidingCacheExpiration;
        protected readonly ISiteMapCache siteMapCache;
        protected readonly ISiteMapCacheKeyGenerator siteMapCacheKeyGenerator;
        protected readonly ISiteMapBuilderSetStrategy siteMapBuilderSetStrategy;
        protected readonly ISiteMapFactory siteMapFactory;
        protected readonly ISiteMapCacheKeyToBuilderSetMapper siteMapCacheKeyToBuilderSetMapper;
        

        protected readonly ReaderWriterLockSlim synclock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);

        public virtual ISiteMap GetSiteMap()
        {
            var key = siteMapCacheKeyGenerator.GenerateKey();
            return GetSiteMap(key);
        }

        public virtual ISiteMap GetSiteMap(string siteMapCacheKey)
        {
            if (String.IsNullOrEmpty(siteMapCacheKey))
            {
                throw new ArgumentNullException("siteMapCacheKey");
            }

            synclock.EnterUpgradeableReadLock();
            try
            {
                ISiteMap siteMap = null;
                if (siteMapCache.TryGetValue(siteMapCacheKey, out siteMap))
                {
                    return siteMap;
                }
                else
                {
                    synclock.EnterWriteLock();
                    try
                    {
                        // Build sitemap
                        var builderSetName = siteMapCacheKeyToBuilderSetMapper.GetBuilderSetName(siteMapCacheKey);
                        var builderSet = siteMapBuilderSetStrategy.GetBuilderSet(builderSetName);
                        siteMap = siteMapFactory.Create(builderSet.Builder);
                        siteMap.BuildSiteMap();

                        siteMapCache.Insert(siteMapCacheKey, siteMap, builderSet.CreateCacheDependency(), absoluteCacheExpiration, slidingCacheExpiration);

                        return siteMap;
                    }
                    finally
                    {
                        synclock.ExitWriteLock();
                    }
                }
            }
            finally
            {
                synclock.ExitUpgradeableReadLock();
            }
        }

        protected virtual void siteMapCache_SiteMapRemoved(object sender, SiteMapCacheItemRemovedEventArgs e)
        {
            // Call clear to remove ISiteMap object references from internal collections. This
            // will release the circular references and free the memory.
            e.SiteMap.Clear();
        }
    }
}