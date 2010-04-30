using System.Collections.Generic;
using System.Linq;
using System.Net;
using Raven.Client.Document;
using Raven.Client.Shard.ShardStrategy;
using Raven.Client.Shard.ShardStrategy.ShardResolution;
using System;

namespace Raven.Client.Shard
{
	public class ShardedDocumentSession : IDocumentSession
	{
		public void Clear()
		{
			foreach (var shardSession in shardSessions)
			{
				shardSession.Clear();
			}
		}

        public bool UseOptimisticConcurrency
        {
            get
            {
                return shardSessions.All(x => x.UseOptimisticConcurrency);
            }
            set
            {
                foreach (var shardSession in shardSessions)
                {
                    shardSession.UseOptimisticConcurrency = value;
                }
            }
        }

	    public event Action<object> Stored;

        public ShardedDocumentSession(IShardStrategy shardStrategy, params IDocumentSession[] shardSessions)
		{
            this.shardStrategy = shardStrategy;
            this.shardSessions = shardSessions;

            foreach (var shardSession in shardSessions)
            {
                shardSession.Stored += Stored;
            }
		}

		private readonly IShardStrategy shardStrategy;
		private readonly IDocumentSession[] shardSessions;

		public T Load<T>(string id)
		{
            var shardIds = shardStrategy.ShardResolutionStrategy.SelectShardIdsFromData(ShardResolutionStrategyData.BuildFrom(typeof(T))) ?? new string[] { };

            IDocumentSession[] shardsToUse = shardSessions.Where(x => shardIds.Contains(x.StoreIdentifier)).ToArray();

            //default to all sessions if none found to use
            if (shardIds.Count == 0)
                shardsToUse = shardSessions;

            //if we can narrow down to single shard, explicitly call it
            if (shardsToUse.Length == 1)
            {
                return shardsToUse[0].Load<T>(id);
            }
			var results = shardStrategy.ShardAccessStrategy.Apply(shardsToUse, x =>
			{
				try
				{
					return new[] {x.Load<T>(id)};
				}
				catch (WebException e)
				{
					var httpWebResponse = e.Response as HttpWebResponse; // we ignore 404, it is expected
					if (httpWebResponse == null || httpWebResponse.StatusCode != HttpStatusCode.NotFound)
						throw;
					return null;
				}
			});

			return results
				.Where(x => ReferenceEquals(null, x) == false)
				.FirstOrDefault();
		}

	    public T[] Load<T>(params string[] ids)
	    {
	        return shardStrategy.ShardAccessStrategy.Apply(shardSessions, sessions => sessions.Load<T>(ids)).ToArray();
	    }

	    public void Delete<T>(T entity)
	    {
            if(ReferenceEquals(entity,null))
                throw new ArgumentNullException("entity");

            var shardIds = shardStrategy.ShardSelectionStrategy.SelectShardIdForExistingObject(entity);

	    	GetSingleShardSession(shardIds).Delete(entity);
	    }

	    private IDocumentSession GetSingleShardSession(string shardId)
        {
			var shardSession = shardSessions.FirstOrDefault(x => x.StoreIdentifier == shardId);
            if (shardSession == null) 
				throw new ApplicationException("Can't find a shard with identifier: " + shardId);
            return shardSession;
        }

		public void Store<T>(T entity)
		{
			string shardId = shardStrategy.ShardSelectionStrategy.SelectShardIdForNewObject(entity);
			if (String.IsNullOrEmpty(shardId))
				throw new ApplicationException("Can't find a shard to use for entity: " + entity);

			GetSingleShardSession(shardId).Store(entity);
		}

		public void Evict<T>(T entity)
		{
			string shardId = shardStrategy.ShardSelectionStrategy.SelectShardIdForExistingObject(entity);
			if (String.IsNullOrEmpty(shardId))
				throw new ApplicationException("Can't find a shard to use for entity: " + entity);

			GetSingleShardSession(shardId).Evict(entity);
		}

		/// <summary>
		/// Note that while we can assume a transaction for a single shard, cross shard transactions will NOT work.
		/// </summary>
		public void SaveChanges()
		{
			foreach (var shardSession in shardSessions)
			{
				shardSession.SaveChanges();
			}
        }

        public IDocumentQuery<T> Query<T>(string indexName)
		{
        	return new ShardedDocumentQuery<T>(indexName, shardSessions);
        }

		public string StoreIdentifier
		{
			get
			{
				return "ShardedSession";
			}
		}

        #region IDisposable Members

        public void Dispose()
        {
            foreach (var shardSession in shardSessions)
                shardSession.Dispose();

            //dereference all event listeners
            Stored = null;
        }

        #endregion

	    public void Commit(Guid txId)
	    {
	        shardStrategy.ShardAccessStrategy.Apply(shardSessions, session =>
	        {
                session.Commit(txId);
	            return new List<int>();
	        });
	    }

	    public void Rollback(Guid txId)
	    {
	        shardStrategy.ShardAccessStrategy.Apply(shardSessions, session =>
	        {
	            session.Rollback(txId);
                return new List<int>();
	        });
	    }
	}
}