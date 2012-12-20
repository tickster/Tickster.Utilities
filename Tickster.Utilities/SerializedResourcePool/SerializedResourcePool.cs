/*
 * Copyright (c) 2012 Markus Olsson, Tickster AB
 * var mail = "developers@tickster.com";
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy of this 
 * software and associated documentation files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use, copy, modify, merge, publish, 
 * distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING 
 * BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
 * DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, 
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Tickster.Caching;
using Tickster.Extensions;

namespace Tickster.Threading
{
    /// <summary>
    /// An implementation of a pool providing serialized (synchronized) access 
    /// and atomic creation (via factory) of its resource objects.
    /// </summary>
    public class SerializedResourcePool<TKey, TValue> : IDisposable where TValue : class
    {
        /// <summary>
        /// Inner dictionary containing all resources.
        /// </summary>
        private Dictionary<TKey, SerializedResource<TKey, TValue>> _resources;

        /// <summary>
        /// The factory method for creating new resources based on the key.
        /// </summary>
        private Func<TKey, TValue> _resourceFactory;

        /// <summary>
        /// Reader writer lock for serialized access to the <see cref="_resources"/> dictionary.
        /// </summary>
        private ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();

        /// <summary>
        /// Timer object used for removing expired resources from the pool.
        /// </summary>
        private Timer _purgeTimer;

        /// <summary>
        /// Indicates whether this pool has been disposed through a call to the Dispose method.
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// Gets or sets a callback for when items are removed from the dictionary
        /// </summary>
        public Action<SerializedResource<TKey, TValue>, RemoveReason> RemoveCallback { get; set; }

        private TimeSpan _resourceLifeTime;

        /// <summary>
        /// Gets or sets the resource life time. Set this to TimeSpan.MaxValue to prevent resources from expiring (default)
        /// </summary>
        /// <value>The resource life time.</value>
        public TimeSpan ResourceLifeTime
        {
            get
            {
                return _resourceLifeTime;
            }
            set
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException("value", "Value must be greater than TimeSpan.Zero. Use TimeSpan.MaxValue to cancel expiration");

                bool reschedule = false;

                if (value < _resourceLifeTime)
                    reschedule = true;

                _resourceLifeTime = value;

                if (reschedule)
                    SchedulePurgeTimer();
            }
        }

        /// <summary>
        /// Gets the number of resources currently residing within the pool
        /// </summary>
        /// <value>The count.</value>
        public int Count
        {
            get
            {
                // TODO: If we maintain a private count variable we can avoid
                // a global read lock here. Need to measure and test if there's any
                // real gain.
                using (_rwLock.GetDisposableReadLock())
                    return _resources.Count;
            }
        }

        /// <summary>
        /// Gets the number of resources currently residing within the pool
        /// </summary>
        /// <value>The count.</value>
        public ICollection<TKey> Keys
        {
            get
            {
                using (_rwLock.GetDisposableReadLock())
                    return _resources.Keys.ToList();
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether to enable lazy resource loading.
        /// Enabling lazy load means that the resource factory will not be called upon to provide a value 
        /// until the first time the Value property of the <see cref="SerializedResource"/> instance is used.
        /// This can lead to unwanted and dangerous side-effects in your code, default value if false and
        /// use of this is not recommended unless there's a strong need for it.
        /// </summary>
        /// <value>
        ///     <c>true</c> if [enable lazy resource loading]; otherwise, <c>false</c>.
        /// </value>
        [DefaultValue(false)]
        public bool EnableLazyResourceLoading { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory)
            : this(resourceFactory, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="removeCallback">The remove callback.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, Action<SerializedResource<TKey, TValue>, RemoveReason> removeCallback)
            : this(resourceFactory, removeCallback, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="resourceLifeTime">The resource life time.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, TimeSpan resourceLifeTime)
            : this(resourceFactory, resourceLifeTime, null, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="comparer">The comparer.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, IEqualityComparer<TKey> comparer)
            : this(resourceFactory, null, comparer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="comparer">The comparer.</param>
        /// <param name="resourceLifeTime">The resource life time.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, IEqualityComparer<TKey> comparer, TimeSpan resourceLifeTime)
            : this(resourceFactory, resourceLifeTime, null, comparer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="removeCallback">The remove callback.</param>
        /// <param name="comparer">The comparer.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, Action<SerializedResource<TKey, TValue>, RemoveReason> removeCallback, IEqualityComparer<TKey> comparer)
            : this(resourceFactory, TimeSpan.MaxValue, removeCallback, comparer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="resourceLifeTime">The resource life time.</param>
        /// <param name="removeCallback">The remove callback.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, TimeSpan resourceLifeTime, Action<SerializedResource<TKey, TValue>, RemoveReason> removeCallback) :
            this(resourceFactory, resourceLifeTime, removeCallback, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializedResourcePool&lt;TKey, TValue&gt;"/> class.
        /// </summary>
        /// <param name="resourceFactory">The resource factory.</param>
        /// <param name="resourceLifeTime">The resource life time.</param>
        /// <param name="removeCallback">The remove callback.</param>
        /// <param name="comparer">The comparer.</param>
        public SerializedResourcePool(Func<TKey, TValue> resourceFactory, TimeSpan resourceLifeTime, Action<SerializedResource<TKey, TValue>, RemoveReason> removeCallback, IEqualityComparer<TKey> comparer)
        {
            if (resourceFactory == null)
                throw new ArgumentNullException("resourceFactory");

            if (comparer == null)
                comparer = EqualityComparer<TKey>.Default;

            if (resourceLifeTime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("resourceLifeTime", "Resource life time must be greater than zero");

            EnableLazyResourceLoading = false;
            RemoveCallback = removeCallback;

            _resourceLifeTime = resourceLifeTime;
            _resources = new Dictionary<TKey, SerializedResource<TKey, TValue>>(comparer);
            _resourceFactory = resourceFactory;
            _purgeTimer = new Timer(PurgeExpiredItems);
        }

        /// <summary>
        /// Gets the resource associated specified key.
        /// </summary>
        /// <param name="key">The key of the resource to get.</param>
        public SerializedResource<TKey, TValue> Get(TKey key)
        {
            return Get(key, touch: true);
        }

        /// <summary>
        /// Gets the resource associated specified key.
        /// </summary>
        /// <param name="key">The key of the resource to get.</param>
        public SerializedResource<TKey, TValue> Get(TKey key, bool touch)
        {
            return Get(key, _resourceFactory, touch);
        }

        /// <summary>
        /// Gets the resource associated specified key.
        /// </summary>
        /// <param name="key">The key of the resource to get.</param>
        /// <param name="factory">The factory to use if the resource isn't found.</param>
        public SerializedResource<TKey, TValue> Get(TKey key, Func<TKey, TValue> factory)
        {
            return Get(key, factory, touch: true);
        }

        /// <summary>
        /// Gets the resource associated specified key.
        /// </summary>
        /// <param name="key">The key of the resource to get.</param>
        /// <param name="factory">The factory to use if the resource isn't found.</param>
        public SerializedResource<TKey, TValue> Get(TKey key, Func<TKey, TValue> factory, bool touch)
        {
            while (true)
            {
                SerializedResource<TKey, TValue> resource = GetOrCreateResource(key, factory);

                if (resource == null)
                    return null;

                resource.Enter();
                try
                {
                    if (!resource.HasExpired)
                    {
                        if (!EnableLazyResourceLoading)
                        {
                            try
                            {
                                resource.AssertValueLoaded();
                            }
                            catch
                            {
                                using (_rwLock.GetDisposableWriteLock())
                                    _resources.Remove(resource.Key);

                                resource.HasExpired = true;
                                throw;
                            }
                        }

                        if (!resource.IsNew && touch)
                            resource.Touch();

                        return resource;
                    }
                }
                catch
                {
                    resource.Return();
                    throw;
                }

                resource.Return();

                // TODO: Should this be a spinwait or some other mecanism?
                // the purpose is to minimize racing when a resource is being
                // expunged.
                Thread.Sleep(1);
            }
        }

        /// <summary>
        /// Returns the specified resource to the pool.
        /// </summary>
        /// <param name="resource">The resource to return.</param>
        public void Return(SerializedResource<TKey, TValue> resource)
        {
            resource.Return();
        }

        /// <summary>
        /// Clears the pool of all its resources.
        /// </summary>
        public void Clear()
        {
            Clear(true);
        }

        /// <summary>
        /// Clears the pool of all its resources and optionally invokes remove callbacks.
        /// </summary>
        public void Clear(bool invokeRemoveCallbacks)
        {
            while (true)
            {
                using (_rwLock.GetDisposableWriteLock())
                {
                    if (_resources.Count == 0)
                        return;

                    foreach (var kv in _resources.ToList())
                    {
                        SerializedResource<TKey, TValue> resource = kv.Value;

                        // If we do .Enter here it's possible that we'll create a deadlock
                        // with .Get() (when a resource factory is failing). It's a small
                        // corner case but we'll stay on the safe side.
                        if (resource.TryEnter(TimeSpan.FromMilliseconds(10)))
                        {
                            try
                            {
                                if (resource.HasExpired)
                                    continue;

                                _resources.Remove(kv.Key);
                                resource.HasExpired = true;

                                if (invokeRemoveCallbacks)
                                    OnRemove(resource, RemoveReason.Cleared);
                            }
                            finally
                            {
                                resource.Return();
                            }
                        }
                    }

                    // Fast path so that we don't have to acquire the write lock once more
                    if (_resources.Count == 0)
                        return;
                }
            }
        }

        /// <summary>
        /// Removes the specified key and fires the remove callback.
        /// </summary>
        /// <param name="key">The key of the resource to remove.</param>
        public bool Remove(TKey key)
        {
            return Remove(key, RemoveReason.ExplicitRemove);
        }

        /// <summary>
        /// Removes the specified key and fires the remove callback with the specified reason.
        /// </summary>
        /// <param name="key">The key of the resource to remove.</param>
        /// <param name="removeReason">The reason for removal.</param>
        private bool Remove(TKey key, RemoveReason removeReason)
        {
            SerializedResource<TKey, TValue> resource = GetOrCreateResource(key, factory: null);

            if (resource == null)
                return false;

            while (true)
            {
                resource.Enter();
                try
                {
                    // Only set when the item has been removed from the dictionary.
                    if (resource.HasExpired)
                        return false;

                    if (_rwLock.TryEnterWriteLock(TimeSpan.FromMilliseconds(10)))
                    {
                        try
                        {
                            if (!_resources.Remove(key))
                                return false;
                        }
                        finally
                        {
                            _rwLock.ExitWriteLock();
                        }
                    }

                    resource.HasExpired = true;
                    OnRemove(resource, removeReason);

                    return true;
                }
                finally
                {
                    resource.Return();
                }
            }
        }

        private void PurgeExpiredItems(object state)
        {
            List<TKey> removeCandidates;

            using (_rwLock.GetDisposableReadLock())
            {
                TimeSpan fuzz = TimeSpan.FromMilliseconds(250);
                DateTime threshold = DateTime.UtcNow - ResourceLifeTime + fuzz;

                removeCandidates = _resources
                    .Where(kv => kv.Value.LastTouchUtc <= threshold)
                    .Select(kv => kv.Key)
                    .ToList();
            }

            foreach (var key in removeCandidates)
            {
                Remove(key, RemoveReason.Expired);
            }

            SchedulePurgeTimer();
        }

        protected virtual void OnRemove(SerializedResource<TKey, TValue> resource, RemoveReason removeReason)
        {
            var callback = RemoveCallback;

            if (callback != null)
                callback(resource, removeReason);
        }

        private SerializedResource<TKey, TValue> GetOrCreateResource(TKey key, Func<TKey, TValue> factory)
        {
            SerializedResource<TKey, TValue> resource;

            using (_rwLock.GetDisposableReadLock())
            {
                if (_resources.TryGetValue(key, out resource))
                {
                    return resource;
                }
            }

            if (factory == null)
                return null;

            using (_rwLock.GetDisposableWriteLock())
            {
                if (!_resources.TryGetValue(key, out resource))
                {
                    resource = CreateResource(key, factory);
                    _resources.Add(key, resource);

                    if (_resources.Count == 1)
                        SchedulePurgeTimer(ResourceLifeTime);
                }

                return resource;
            }
        }

        private void SchedulePurgeTimer()
        {
            using (_rwLock.GetDisposableReadLock())
            {
                if (_resources.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    DateTime nextExpiration = _resources.Values.Select(r => r.LastTouchUtc + _resourceLifeTime).Min();
                    SchedulePurgeTimer(nextExpiration - now);
                }
            }
        }

        private void SchedulePurgeTimer(TimeSpan dueTime)
        {
            TimeSpan minValue = TimeSpan.FromMilliseconds(750);

            if (dueTime < minValue)
                dueTime = minValue;

            _purgeTimer.Change(dueTime, TimeSpan.FromMilliseconds(-1));
        }

        private SerializedResource<TKey, TValue> CreateResource(TKey key, Func<TKey, TValue> factory)
        {
            return new SerializedResource<TKey, TValue>(key, factory, this);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _purgeTimer.Dispose();
                _purgeTimer = null;

                _rwLock.Dispose();
                _rwLock = null;

                _disposed = true;
            }
        }
    }
}