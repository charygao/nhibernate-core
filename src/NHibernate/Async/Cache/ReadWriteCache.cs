﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by AsyncGenerator.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------


using System;
using System.Collections;
using NHibernate.Cache.Access;

namespace NHibernate.Cache
{
	using System.Threading.Tasks;
	using System.Threading;
	public partial class ReadWriteCache : ICacheConcurrencyStrategy
	{
		private readonly NHibernate.Util.AsyncLock _lockObjectAsync = new NHibernate.Util.AsyncLock();

		/// <summary>
		/// Do not return an item whose timestamp is later than the current
		/// transaction timestamp. (Otherwise we might compromise repeatable
		/// read unnecessarily.) Do not return an item which is soft-locked.
		/// Always go straight to the database instead.
		/// </summary>
		/// <remarks>
		/// Note that since reading an item from that cache does not actually
		/// go to the database, it is possible to see a kind of phantom read
		/// due to the underlying row being updated after we have read it
		/// from the cache. This would not be possible in a lock-based
		/// implementation of repeatable read isolation. It is also possible
		/// to overwrite changes made and committed by another transaction
		/// after the current transaction read the item from the cache. This
		/// problem would be caught by the update-time version-checking, if 
		/// the data is versioned or timestamped.
		/// </remarks>
		public async Task<object> GetAsync(CacheKey key, long txTimestamp, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (await _lockObjectAsync.LockAsync())
			{
				if (log.IsDebugEnabled)
				{
					log.Debug("Cache lookup: " + key);
				}

				// commented out in H3.1
				/*try
				{
					cache.Lock( key );*/

				ILockable lockable = (ILockable) await (cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);

				bool gettable = lockable != null && lockable.IsGettable(txTimestamp);

				if (gettable)
				{
					if (log.IsDebugEnabled)
					{
						log.Debug("Cache hit: " + key);
					}

					return ((CachedItem) lockable).Value;
				}
				else
				{
					if (log.IsDebugEnabled)
					{
						if (lockable == null)
						{
							log.Debug("Cache miss: " + key);
						}
						else
						{
							log.Debug("Cached item was locked: " + key);
						}
					}
					return null;
				}
				/*}
				finally
				{
					cache.Unlock( key );
				}*/
			}
		}

		/// <summary>
		/// Stop any other transactions reading or writing this item to/from
		/// the cache. Send them straight to the database instead. (The lock
		/// does time out eventually.) This implementation tracks concurrent
		/// locks by transactions which simultaneously attempt to write to an
		/// item.
		/// </summary>
		public async Task<ISoftLock> LockAsync(CacheKey key, object version, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (await _lockObjectAsync.LockAsync())
			{
				if (log.IsDebugEnabled)
				{
					log.Debug("Invalidating: " + key);
				}

				try
				{
					await (cache.LockAsync(key, cancellationToken)).ConfigureAwait(false);

					ILockable lockable = (ILockable) await (cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);
					long timeout = cache.NextTimestamp() + cache.Timeout;
					CacheLock @lock = lockable == null ?
					                  new CacheLock(timeout, NextLockId(), version) :
					                  lockable.Lock(timeout, NextLockId());
					await (cache.PutAsync(key, @lock, cancellationToken)).ConfigureAwait(false);
					return @lock;
				}
				finally
				{
					await (cache.UnlockAsync(key, cancellationToken)).ConfigureAwait(false);
				}
			}
		}

		/// <summary>
		/// Do not add an item to the cache unless the current transaction
		/// timestamp is later than the timestamp at which the item was
		/// invalidated. (Otherwise, a stale item might be re-added if the
		/// database is operating in repeatable read isolation mode.)
		/// </summary>
		/// <returns>Whether the item was actually put into the cache</returns>
		public async Task<bool> PutAsync(CacheKey key, object value, long txTimestamp, object version, IComparer versionComparator,
		                bool minimalPut, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (txTimestamp == long.MinValue)
			{
				// MinValue means cache is disabled
				return false;
			}

			using (await _lockObjectAsync.LockAsync())
			{
				if (log.IsDebugEnabled)
				{
					log.Debug("Caching: " + key);
				}

				try
				{
					await (cache.LockAsync(key, cancellationToken)).ConfigureAwait(false);

					ILockable lockable = (ILockable) await (cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);

					bool puttable = lockable == null ||
					                lockable.IsPuttable(txTimestamp, version, versionComparator);

					if (puttable)
					{
						await (cache.PutAsync(key, new CachedItem(value, cache.NextTimestamp(), version), cancellationToken)).ConfigureAwait(false);
						if (log.IsDebugEnabled)
						{
							log.Debug("Cached: " + key);
						}
						return true;
					}
					else
					{
						if (log.IsDebugEnabled)
						{
							if (lockable.IsLock)
							{
								log.Debug("Item was locked: " + key);
							}
							else
							{
								log.Debug("Item was already cached: " + key);
							}
						}
						return false;
					}
				}
				finally
				{
					await (cache.UnlockAsync(key, cancellationToken)).ConfigureAwait(false);
				}
			}
		}

		/// <summary>
		/// decrement a lock and put it back in the cache
		/// </summary>
		private Task DecrementLockAsync(object key, CacheLock @lock, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object>(cancellationToken);
			}
			try
			{
				//decrement the lock
				@lock.Unlock(cache.NextTimestamp());
				return cache.PutAsync(key, @lock, cancellationToken);
			}
			catch (Exception ex)
			{
				return Task.FromException<object>(ex);
			}
		}

		public async Task ReleaseAsync(CacheKey key, ISoftLock clientLock, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (await _lockObjectAsync.LockAsync())
			{
				if (log.IsDebugEnabled)
				{
					log.Debug("Releasing: " + key);
				}

				try
				{
					await (cache.LockAsync(key, cancellationToken)).ConfigureAwait(false);

					ILockable lockable = (ILockable) await (cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);
					if (IsUnlockable(clientLock, lockable))
					{
						await (DecrementLockAsync(key, (CacheLock) lockable, cancellationToken)).ConfigureAwait(false);
					}
					else
					{
						await (HandleLockExpiryAsync(key, cancellationToken)).ConfigureAwait(false);
					}
				}
				finally
				{
					await (cache.UnlockAsync(key, cancellationToken)).ConfigureAwait(false);
				}
			}
		}

		internal Task HandleLockExpiryAsync(object key, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object>(cancellationToken);
			}
			try
			{
				log.Warn("An item was expired by the cache while it was locked (increase your cache timeout): " + key);
				long ts = cache.NextTimestamp() + cache.Timeout;
				// create new lock that times out immediately
				CacheLock @lock = new CacheLock(ts, NextLockId(), null);
				@lock.Unlock(ts);
				return cache.PutAsync(key, @lock, cancellationToken);
			}
			catch (Exception ex)
			{
				return Task.FromException<object>(ex);
			}
		}

		public Task ClearAsync(CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object>(cancellationToken);
			}
			return cache.ClearAsync(cancellationToken);
		}

		public Task RemoveAsync(CacheKey key, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object>(cancellationToken);
			}
			return cache.RemoveAsync(key, cancellationToken);
		}

		/// <summary>
		/// Re-cache the updated state, if and only if there there are
		/// no other concurrent soft locks. Release our lock.
		/// </summary>
		public async Task<bool> AfterUpdateAsync(CacheKey key, object value, object version, ISoftLock clientLock, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (await _lockObjectAsync.LockAsync())
			{
				if (log.IsDebugEnabled)
				{
					log.Debug("Updating: " + key);
				}

				try
				{
					await (cache.LockAsync(key, cancellationToken)).ConfigureAwait(false);

					ILockable lockable = (ILockable) await (cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);
					if (IsUnlockable(clientLock, lockable))
					{
						CacheLock @lock = (CacheLock) lockable;
						if (@lock.WasLockedConcurrently)
						{
							// just decrement the lock, don't recache
							// (we don't know which transaction won)
							await (DecrementLockAsync(key, @lock, cancellationToken)).ConfigureAwait(false);
						}
						else
						{
							//recache the updated state
							await (cache.PutAsync(key, new CachedItem(value, cache.NextTimestamp(), version), cancellationToken)).ConfigureAwait(false);
							if (log.IsDebugEnabled)
							{
								log.Debug("Updated: " + key);
							}
						}
						return true;
					}
					else
					{
						await (HandleLockExpiryAsync(key, cancellationToken)).ConfigureAwait(false);
						return false;
					}
				}
				finally
				{
					await (cache.UnlockAsync(key, cancellationToken)).ConfigureAwait(false);
				}
			}
		}

		public async Task<bool> AfterInsertAsync(CacheKey key, object value, object version, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (await _lockObjectAsync.LockAsync())
			{
				if (log.IsDebugEnabled)
				{
					log.Debug("Inserting: " + key);
				}

				try
				{
					await (cache.LockAsync(key, cancellationToken)).ConfigureAwait(false);

					ILockable lockable = (ILockable) await (cache.GetAsync(key, cancellationToken)).ConfigureAwait(false);
					if (lockable == null)
					{
						await (cache.PutAsync(key, new CachedItem(value, cache.NextTimestamp(), version), cancellationToken)).ConfigureAwait(false);
						if (log.IsDebugEnabled)
						{
							log.Debug("Inserted: " + key);
						}
						return true;
					}
					else
					{
						return false;
					}
				}
				finally
				{
					await (cache.UnlockAsync(key, cancellationToken)).ConfigureAwait(false);
				}
			}
		}

		public Task EvictAsync(CacheKey key, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<object>(cancellationToken);
			}
			try
			{
				Evict(key);
				return Task.CompletedTask;
			}
			catch (Exception ex)
			{
				return Task.FromException<object>(ex);
			}
		}

		public Task<bool> UpdateAsync(CacheKey key, object value, object currentVersion, object previousVersion, CancellationToken cancellationToken)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return Task.FromCanceled<bool>(cancellationToken);
			}
			try
			{
				return Task.FromResult<bool>(Update(key, value, currentVersion, previousVersion));
			}
			catch (Exception ex)
			{
				return Task.FromException<bool>(ex);
			}
		}
	}
}