using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace AsyncScrolling
{
    public enum SyncMode { Default, Sequential };

    /// <summary>
    /// Data Grid, ListView 컨트롤 등에 바인딩 처리시 화면에 보이는 영역만큼 지정된 개수로 서버에 데이터를 요청하여 표시해 주는 Collection 클래스
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class MirrorCollection<T> : INotifyCollectionChanged, IList, IList<T> where T : class, new()
    {
        public delegate void InsertKeysTaskEndEventHandler(object sender);
        public delegate void ResolveFetchDataTaskEventHandler<T>(object sender, ResolveTaskEventArgs<T> e);
        public delegate void DataLoadedEventHandler(object sender, DataLoadedEventArgs e);

        #region Event Fields
        /// <summary>
        /// Insert Keys 작업이 완료 된 후 이벤트 발생
        /// </summary>
        public event InsertKeysTaskEndEventHandler InsertKeysTaskEnd;
        /// <summary>
        /// 데이터 요청 Task 이벤트 발생
        /// </summary>
        public event ResolveFetchDataTaskEventHandler<T> ResolveFetchDataTask;
        /// <summary>
        /// 데이터가 모두 로드 되었을때 이벤트 발생
        /// </summary>
        public event DataLoadedEventHandler DataLoaded;
        #endregion

        #region Member Fields
        /// <summary>
        /// 전체 데이터
        /// Key : 데이터 관리 Key
        /// Value : 데이터
        /// </summary>
        private Dictionary<object, Wrapper> _innerStorage;
        /// <summary>
        /// 전체 데이터의 Key List
        /// </summary>
        private List<object> _innerKeyStorage;
        private int _maxConcurrentRequests;
        private int _batchAmount;

        private List<Wrapper> _pendingRequests;
        private List<Wrapper> _requestedObjects;
        private int _currentRequestsNumber;
        #endregion

        #region Constructor
        /// <summary>
        /// MirrorCollection 인스턴스를 기본 값으로 초기화 합니다.
        /// </summary>
        /// <param name="keyPropertyName">데이터의 Key 속성 이름</param>
        public MirrorCollection(string keyPropertyName)
        {
            KeyPropertyName = keyPropertyName;
            CollectionSyncMode = SyncMode.Default;
            BatchAmount = 10;
            UseBatchReplace = true;
            UseAsyncCollectionChanged = false;
            MaxConcurrentRequests = Environment.ProcessorCount;

            _innerStorage = new Dictionary<object, Wrapper>();
            _innerKeyStorage = new List<object>();
            _pendingRequests = new List<Wrapper>();
            _requestedObjects = new List<Wrapper>();
        }

        /// <summary>
        /// MirrorCollection 인스턴스를 초기화 합니다.
        /// </summary>
        /// <param name="keyPropertyName">데이터의 Key 속성 이름</param>
        /// <param name="collectionSyncMode">데이터 요청시 진행 모드</param>
        public MirrorCollection(string keyPropertyName, SyncMode collectionSyncMode)
            : this(keyPropertyName)
        {
            CollectionSyncMode = collectionSyncMode;
        }

        /// <summary>
        /// MirrorCollection 인스턴스를 초기화 합니다.
        /// </summary>
        /// <param name="keyPropertyName">데이터의 Key 속성 이름</param>
        /// <param name="collectionSyncMode">데이터 요청시 진행 모드</param>
        /// <param name="batchAmount">한번 요청시 받아올 데이터 개수</param>
        /// <param name="maxConcurrentRequests">최대 동시 요청할 수 있는 수</param>
        public MirrorCollection(string keyPropertyName, SyncMode collectionSyncMode, int batchAmount, int maxConcurrentRequests)
            : this(keyPropertyName, collectionSyncMode)
        {
            BatchAmount = batchAmount;
            MaxConcurrentRequests = maxConcurrentRequests;
        }
        #endregion

        #region Properties
        /// <summary>
        /// 데이터의 Key 속성 이름
        /// </summary>
        public string KeyPropertyName { get; private set; }
        /// <summary>
        /// 데이터 요청시 진행 모드
        /// </summary>
        public SyncMode CollectionSyncMode { get; set; }
        public bool UseBatchReplace { get; set; }
        /// <summary>
        /// 비동기로 데이터 리스트 변경 작업 반영 여부
        /// </summary>
        public bool UseAsyncCollectionChanged { get; set; }

        /// <summary>
        /// 한번 요청시 받아올 데이터 개수
        /// </summary>
        public int BatchAmount
        {
            get { return _batchAmount; }
            set
            {
                _batchAmount = value;
                CheckBatchAmount();
            }
        }

        /// <summary>
        /// 최대 동시 요청할 수 있는 수
        /// </summary>
        public int MaxConcurrentRequests
        {
            get { return _maxConcurrentRequests; }
            set
            {
                _maxConcurrentRequests = value;
                CheckMaxConcurrentRequests();
            }
        }
        #endregion

        private void CheckBatchAmount()
        {
            if (BatchAmount < 1)
                BatchAmount = 1;
        }

        private void CheckMaxConcurrentRequests()
        {
            if (MaxConcurrentRequests == -1)
                return;

            if (MaxConcurrentRequests < 2)
                MaxConcurrentRequests = 2;
        }

        private void CheckKey(object key)
        {
            if (key == null || _innerStorage.ContainsKey(key))
                throw new NotSupportedException("Null and duplicate keys aren't supported");
        }

        private object GetItemKey(T item)
        {
            if (item == null)
                return null;

            return PropertyHelper<T>.GetPropertyValue(item, KeyPropertyName);
        }

        protected bool IsRequested(Wrapper wrapper)
        {
            return _pendingRequests.Contains(wrapper) || _requestedObjects.Contains(wrapper);
        }

        protected async void PerformBatchTaskAsync(Task<List<T>> batchTask)
        {
#if DEBUG
            System.Diagnostics.Stopwatch st = new System.Diagnostics.Stopwatch();
            st.Start();
#endif

            //System.Threading.Thread.Sleep(10);
            _currentRequestsNumber++;
            List<T> results = await batchTask;
            _currentRequestsNumber--;

            List<Wrapper> loadedWrappers = new List<Wrapper>();
            List<T> newDataObjects = new List<T>();
            List<T> oldDataObjects = new List<T>();
            int startingIndex = -1;

            foreach (T loadedItem in results)
            {
                Wrapper loadedItemWrapper = _innerStorage[GetItemKey(loadedItem)];

                if (startingIndex == -1 && UseBatchReplace)
                {
                    startingIndex = _innerKeyStorage.IndexOf(loadedItemWrapper.Key);
                }
                oldDataObjects.Add(loadedItemWrapper.DataObject);
                newDataObjects.Add(loadedItem);

                loadedItemWrapper.DataObject = loadedItem;
                loadedItemWrapper.IsLoaded = true;

                loadedWrappers.Add(loadedItemWrapper);

                if (!UseBatchReplace)
                {
                    int replaceIndex = _innerKeyStorage.IndexOf(loadedItemWrapper.Key);
                    this.RaiseCollectionChangedReplace(newDataObjects.Last(), oldDataObjects.Last(), replaceIndex);
                }
            }

            // 지정 인덱스 크기 만큼 데이터를 받아 왔다면 컨트롤에 데이터 리스트 변경 통보 이벤트 발생
            if (UseBatchReplace)
                this.RaiseCollectionChangedReplace(newDataObjects, oldDataObjects, startingIndex);

            this.CompleteBatchRequest(loadedWrappers);

#if DEBUG
            st.Stop();
            System.Diagnostics.Trace.WriteLine("PerformBatchTaskAsync : " + st.ElapsedMilliseconds);
            //App.Log.Debug("PerformBatchTaskAsync : " + st.ElapsedMilliseconds);
#endif
        }

        protected void StartBatchRequest(Wrapper startWrapper)
        {
            int startIndex = _innerKeyStorage.IndexOf(startWrapper.Key);
            List<Wrapper> objectWrappersToRequest = new List<Wrapper>();

            for (int i = startIndex; i < Math.Min(startIndex + BatchAmount, _innerStorage.Count); i++)
            {
                Wrapper wrapper = _innerStorage[_innerKeyStorage[i]];
                if (!IsRequested(wrapper))
                {
                    objectWrappersToRequest.Add(wrapper);
                }
            }

            if (CollectionSyncMode == SyncMode.Sequential && _requestedObjects.Count > 0)
            {
                _pendingRequests.AddRange(objectWrappersToRequest);
                return;
            }

            if (CollectionSyncMode == SyncMode.Default && MaxConcurrentRequests != -1 && _currentRequestsNumber > MaxConcurrentRequests)
            {
                _pendingRequests.AddRange(objectWrappersToRequest);
                return;
            }

            ResolveTaskEventArgs<T> args = new ResolveTaskEventArgs<T>(objectWrappersToRequest.Select(w => w.Key).ToList());
            this.RaiseFetchDataTask(args);

            if (args.FetchDataTask != null)
            {
                _requestedObjects.AddRange(objectWrappersToRequest);
                this.PerformBatchTaskAsync(args.FetchDataTask);

                /*
                Task.Run(() =>
                {
                    try
                    {
                        this.PerformBatchTaskAsync(args.FetchDataTask);
                    }
                    catch (System.NotSupportedException notSupportedEx)
                    {
                        //
                    }
                    catch (Exception ex)
                    {
                        App.Log.Error(ex);
                    }
                });
                 * */
            }
        }

        protected void CompleteBatchRequest(IList<Wrapper> loadedObjectWrappers)
        {
            // _requestedObjects의 아이템을 조건에 맞는게 제거할 경우
            // loadedObjectWrappers 리스트에 포함 되어 있는지 체크하는 것 은
            // 대량의 데이터일 경우 CPU 오버헤드가 너무 크기 때문에 단순히 IsLoaded속성으로 체크 하도록 변경할 필요가 있다.  [2015. 10. 19 엄태영]

            // 대용량의 데이터를 표시 할 경우 아래 조건은 CPU 오버헤드가 높고 속도가 너무 느림.
            // 기본 데이터량 표시 용도 조건
            //_requestedObjects.RemoveAll(w => loadedObjectWrappers.Contains(w));

            // 대용량의 데이터를 표시 할 경우 아래 조건으로 비교
            _requestedObjects.RemoveAll(w => w.IsLoaded);

            this.RaiseDataLoaded(loadedObjectWrappers.Select(w => w.Key).ToList());

            if (_pendingRequests.Count > 0)
            {
                Wrapper firstPendingWrapper = _pendingRequests[0];
                for (int i = Math.Min(BatchAmount, _pendingRequests.Count) - 1; i >= 0; i--)
                    _pendingRequests.RemoveAt(i);

                this.StartBatchRequest(firstPendingWrapper);
            }
        }

        /// <summary>
        /// Insert Keys 작업이 완료 된 후 이벤트 발생
        /// </summary>
        /// <param name="loadedKeys"></param>
        protected void RaiseInsertKeysTaskEnd()
        {
            if (InsertKeysTaskEnd != null)
                InsertKeysTaskEnd(this);
        }

        protected void RaiseDataLoaded(IList<object> loadedKeys)
        {
            if (DataLoaded != null)
                DataLoaded(this, new DataLoadedEventArgs(loadedKeys));
        }

        /// <summary>
        /// 데이터 요청 Task 이벤트 발생
        /// </summary>
        /// <param name="args"></param>
        protected void RaiseFetchDataTask(ResolveTaskEventArgs<T> args)
        {
            if (ResolveFetchDataTask != null)
                ResolveFetchDataTask(this, args);
        }

        /// <summary>
        /// 데이터의 전체 키 추가
        /// </summary>
        /// <param name="count">총 키 개수 (전체 데이터 개수)</param>
        /// <param name="GetKeyValue">데이터 카를 받아오는 메서드</param>
        public void AddKeys(int count, Func<int, object> GetKeyValue)
        {
            this.InsertKeys(_innerKeyStorage.Count, count, GetKeyValue);
        }

        public void InsertKeys(int index, int count, Func<int, object> GetKeyValue)
        {
            Task.Factory.StartNew((arg) =>
            {
                Tuple<int, Func<int, object>, int> argTuple = (Tuple<int, Func<int, object>, int>)arg;
                int itemCount = argTuple.Item1;
                Func<int, object> getKeyValueFunc = argTuple.Item2;
                int insertIndex = argTuple.Item3;

                for (int i = 0; i < itemCount; i++)
                {
                    object key = getKeyValueFunc(i);
                    this.CheckKey(key);

                    Wrapper newObjectWrapper = new Wrapper(key, KeyPropertyName);
                    _innerStorage.Add(newObjectWrapper.Key, newObjectWrapper);
                    _innerKeyStorage.Insert(insertIndex + i, newObjectWrapper.Key);

                    this.RaiseCollectionChangedAdd(null, insertIndex + i);
                }
            }, Tuple.Create(count, GetKeyValue, index))
            .ContinueWith((tsk) =>
            {
                if (tsk.IsCompleted)
                {
                    this.RaiseInsertKeysTaskEnd();
                }
            });
        }

        public void Refresh(T item, bool isLoaded)
        {
            Wrapper wrapper = _innerStorage[GetItemKey(item)];
            if (wrapper != null)
            {
                wrapper.IsLoaded = isLoaded;
                RaiseCollectionChangedReplace(item, item, _innerKeyStorage.IndexOf(wrapper.Key));
            }
        }

        #region INotifyCollectionChanged Implementation
        public virtual event NotifyCollectionChangedEventHandler CollectionChanged;

        private async void RaiseCollectionChangedAsync(NotifyCollectionChangedEventArgs args)
        {
            Dispatcher dispatcher = null;
            if (Application.Current != null)
                dispatcher = Application.Current.Dispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                await Task.Factory.FromAsync<object, NotifyCollectionChangedEventArgs>(CollectionChanged.BeginInvoke, CollectionChanged.EndInvoke, this, args, new object());
            }
            else
            {
                await dispatcher.BeginInvoke(new Action(() =>
                {
                    Task.Factory.FromAsync<object, NotifyCollectionChangedEventArgs>(CollectionChanged.BeginInvoke, CollectionChanged.EndInvoke, this, args, new object());
                }));
            }
        }

        protected virtual void RaiseCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            try
            {
                if (CollectionChanged == null)
                    return;

                Dispatcher dispatcher = null;
                if (Application.Current != null)
                    dispatcher = Application.Current.Dispatcher;

                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    Action<object, NotifyCollectionChangedEventArgs> dispatcherAction = new Action<object, NotifyCollectionChangedEventArgs>((sender, e) => { CollectionChanged(sender, e); });
                    if (UseAsyncCollectionChanged)
                    {
                        dispatcher.BeginInvoke(dispatcherAction, this, args);
                    }
                    else
                    {
                        dispatcher.Invoke(dispatcherAction, this, args);
                    }

                    return;
                }

                if (UseAsyncCollectionChanged)
                {
                    this.RaiseCollectionChangedAsync(args);
                }
                else
                {
                    foreach (NotifyCollectionChangedEventHandler handler in CollectionChanged.GetInvocationList())
                    {
                        // 2015. 10. 15 엄태영
                        // NotifyCollectionChangedEventHandler를 참조한 개체가 CollectionView 타입일 경우 Refresh() 메서드를 호출해 준다.
                        // CollectionView 개체는 NotifyCollectionChangedEventHandler 이벤트를 지원하지 않아 NotSupportedException가 발생된다.
                        if (handler.Target is CollectionView && handler.Target != null)
                        {
                            ((CollectionView)handler.Target).Refresh();
                        }
                        else
                        {
                            handler(this, args);
                        }
                    }

                    //this.CollectionChanged(this, args);
                }
            }
            catch (NotSupportedException notSupportedEx)
            {
                // 오류 발생시 로그 기록
                //App.Log.Write("MirrorCollection<T> NotSupportedException 오류 발생!");
                //App.Log.Error(notSupportedEx);
            }
            catch (Exception ex)
            {
                //App.Log.Error(ex);
            }
        }

        protected void RaiseCollectionChangedAdd(object newItem, int itemIndex)
        {
            this.RaiseCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItem, itemIndex));
        }

        protected void RaiseCollectionChangedRemove(object oldItem, int itemIndex)
        {
            this.RaiseCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, itemIndex));
        }

        protected void RaiseCollectionChangedReplace(object newItem, object oldItem, int itemIndex)
        {
            this.RaiseCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItem, oldItem, itemIndex));
        }

        protected void RaiseCollectionChangedReplace(IList newItems, IList oldItems, int startingIndex)
        {
            this.RaiseCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItems, oldItems, startingIndex));
        }

        protected void RaiseCollectionChangedReset()
        {
            this.RaiseCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
        #endregion

        #region IList<T> Implementation
        public int IndexOf(T item)
        {
            object key = GetItemKey(item);
            if (key == null)
                return -1;

            return _innerKeyStorage.IndexOf(key);
        }

        public void Insert(int index, T item)
        {
            if (item == null)
                return;

            object key = GetItemKey(item);
            this.CheckKey(key);

            Wrapper wrapper = new Wrapper(key, KeyPropertyName);
            wrapper.DataObject = item;
            wrapper.IsLoaded = true;

            _innerKeyStorage.Insert(index, key);
            _innerStorage.Add(key, wrapper);

            this.RaiseCollectionChangedAdd(wrapper.DataObject, index);
        }

        public void RemoveAt(int index)
        {
            object key = _innerKeyStorage[index];
            T item = _innerStorage[key].DataObject;
            _innerStorage.Remove(key);
            _innerKeyStorage.RemoveAt(index);

            this.RaiseCollectionChangedRemove(item, index);
        }

        public T this[int index]
        {
            get
            {
                // DataGrid 또는 ListView 컨트롤에서 화면에 보이는 영역의 데이터를 표시 할때
                // ※ 바인딩 된 컨트롤에서 IList의 IList.this[int index]인덱서를 자동 호출하게 된다.
                Wrapper wrapper = _innerStorage[_innerKeyStorage[index]];
                // 데이터가 로드 되지 않은 데이터 일 경우 데이터 요청
                if (!wrapper.IsLoaded && !this.IsRequested(wrapper))
                    StartBatchRequest(wrapper);

                return wrapper.DataObject;
            }
            set
            {
                Wrapper wrapper = _innerStorage[_innerKeyStorage[index]];
                wrapper.Key = GetItemKey(value);
                wrapper.IsLoaded = true;

                if (this.IsRequested(wrapper))
                {
                    _requestedObjects.Remove(wrapper);
                    _pendingRequests.Remove(wrapper);
                }
                wrapper.DataObject = value;
            }
        }
        #endregion

        #region ICollection<T> Implementation
        public void Add(T item)
        {
            this.Insert(_innerKeyStorage.Count, item);
        }

        public void Clear()
        {
            _innerStorage.Clear();
            _innerKeyStorage.Clear();

            this.RaiseCollectionChangedReset();
        }

        public bool Contains(T item)
        {
            object key = GetItemKey(item);
            if (key == null)
                return false;

            return _innerStorage.ContainsKey(key);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _innerStorage.Select(kvp => kvp.Value.DataObject).ToList().CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            int itemIndex = IndexOf(item);
            if (itemIndex == -1)
                return false;

            this.RemoveAt(itemIndex);
            return true;
        }

        public int Count
        {
            get { return _innerStorage.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }
        #endregion

        #region IList Implementation
        int IList.Add(object value)
        {
            this.Add((T)value);
            return Count - 1;
        }

        void IList.Clear()
        {
            this.Clear();
        }

        bool IList.Contains(object value)
        {
            return this.Contains((T)value);
        }

        int IList.IndexOf(object value)
        {
            return IndexOf((T)value);
        }

        void IList.Insert(int index, object value)
        {
            this.Insert(index, (T)value);
        }

        bool IList.IsFixedSize
        {
            get { return false; }
        }

        bool IList.IsReadOnly
        {
            get { return false; }
        }

        void IList.Remove(object value)
        {
            this.Remove((T)value);
        }

        void IList.RemoveAt(int index)
        {
            this.RemoveAt(index);
        }

        object IList.this[int index]
        {
            get { return this[index]; }
            set { this[index] = (T)value; }
        }
        #endregion

        #region ICollection Implementation
        void ICollection.CopyTo(Array array, int index)
        {
            ((ICollection)_innerStorage.Select(kvp => kvp.Value.DataObject).ToList()).CopyTo(array, index);
        }

        int ICollection.Count
        {
            get { return Count; }
        }

        bool ICollection.IsSynchronized
        {
            get { return ((ICollection)_innerKeyStorage).IsSynchronized; }
        }

        object ICollection.SyncRoot
        {
            get { return ((ICollection)_innerKeyStorage).SyncRoot; }
        }
        #endregion

        #region IEnumerable<T> Members
        public IEnumerator<T> GetEnumerator()
        {
            return new MirrorCollectionEnumerator(this);
        }
        #endregion

        #region IEnumerable Implementation
        IEnumerator IEnumerable.GetEnumerator()
        {
            return new MirrorCollectionEnumerator(this);
        }
        #endregion


        protected class Wrapper
        {
            private T _dataObject;
            private string _keyPropertyName;

            public Wrapper(object key, string keyPropertyName)
            {
                Key = key;
                this._keyPropertyName = keyPropertyName;
            }

            public object Key { get; set; }
            public bool IsLoaded { get; set; }

            public T DataObject
            {
                get
                {
                    if (_dataObject == null)
                    {
                        //_dataObject = Activator.CreateInstance<T>();
                        _dataObject = new T();
                        PropertyHelper<T>.SetPropertyValue(_dataObject, _keyPropertyName, Key);
                    }
                    return _dataObject;
                }
                set { _dataObject = value; }
            }

        }

        protected class MirrorCollectionEnumerator : IEnumerator<T>, IEnumerator
        {
            private MirrorCollection<T> _owner;
            private int _index;
            private T _current;

            public MirrorCollectionEnumerator(MirrorCollection<T> owner)
            {
                _owner = owner;
                _index = -1;
                _current = default(T);
            }

            #region IEnumerator<T> Implementation
            public T Current
            {
                get { return _current; }
            }
            #endregion

            #region IEnumerator Implementation
            object IEnumerator.Current
            {
                get { return _current; }
            }

            public bool MoveNext()
            {
                _index++;
                if (_index < _owner.Count)
                {
                    _current = _owner[_index];
                    return true;
                }

                _current = default(T);
                return false;
            }

            public void Reset()
            {
                _index = -1;
                _current = default(T);
            }
            #endregion

            #region IDisposable Implementation
            public void Dispose()
            {
                _current = default(T);
                _owner = null;
            }
            #endregion
        }
    }

    public class ResolveTaskEventArgs<T> : EventArgs
    {
        public ResolveTaskEventArgs(IList<object> keyValues)
        {
            Keys = keyValues;
        }

        public IList<object> Keys { get; private set; }
        public Task<List<T>> FetchDataTask { get; set; }
    }

    public class DataLoadedEventArgs : EventArgs
    {
        public DataLoadedEventArgs(IList<object> keyValues)
        {
            Keys = keyValues;
        }

        public IList<object> Keys { get; private set; }
    }

    public static class PropertyHelper<T> where T : class
    {
        private static Type _lastType;
        private static PropertyInfo _propertyInfoCache;

        private static void CheckPropertyInfoCache(string propertyName)
        {
            if (_lastType != typeof(T) || _propertyInfoCache == null || _propertyInfoCache.Name != propertyName)
            {
                _propertyInfoCache = typeof(T).GetRuntimeProperty(propertyName);
                _lastType = typeof(T);
            }
        }

        public static object GetPropertyValue(T dataObject, string propertyName)
        {
            CheckPropertyInfoCache(propertyName);

            if (_propertyInfoCache == null)
                return default(T);

            return _propertyInfoCache.GetValue(dataObject);
        }

        public static void SetPropertyValue(T dataObject, string propertyName, object value)
        {
            CheckPropertyInfoCache(propertyName);

            if (_propertyInfoCache != null)
                _propertyInfoCache.SetValue(dataObject, value);
        }
    }
}
