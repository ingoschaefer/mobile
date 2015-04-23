﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Toggl.Phoebe.Data.DataObjects;
using Toggl.Phoebe.Data.Json.Converters;
using Toggl.Phoebe.Data.Utils;
using Toggl.Phoebe.Logging;
using Toggl.Phoebe.Net;
using XPlatUtils;

namespace Toggl.Phoebe.Data.Views
{
    /// <summary>
    /// This view combines IDataStore data and data from ITogglClient for time views. It tries to load data from
    /// web, but always falls back to data from the local store.
    /// </summary>
    public class AllTimeEntriesViewModel : ICollectionDataView<object>, IDisposable
    {
        private static readonly string Tag = "AllTimeEntriesView";
        private readonly List<DateGroup> dateGroups = new List<DateGroup> ();
        private UpdateMode updateMode = UpdateMode.Batch;
        private DateTime startFrom;
        private Subscription<DataChangeMessage> subscriptionDataChange;
        private Subscription<SyncFinishedMessage> subscriptionSyncFinished;
        private bool reloadScheduled;
        private bool isLoading;
        private bool hasMore;
        private int lastItemNumber;

        public AllTimeEntriesViewModel ()
        {
            var bus = ServiceContainer.Resolve<MessageBus> ();
            subscriptionDataChange = bus.Subscribe<DataChangeMessage> (OnDataChange);
            HasMore = true;
            Reload ();
        }

        public void Dispose ()
        {
            var bus = ServiceContainer.Resolve<MessageBus> ();
            if (subscriptionDataChange != null) {
                bus.Unsubscribe (subscriptionDataChange);
                subscriptionDataChange = null;
            }
            if (subscriptionSyncFinished != null) {
                bus.Unsubscribe (subscriptionSyncFinished);
                subscriptionSyncFinished = null;
            }
        }

        private void OnDataChange (DataChangeMessage msg)
        {
            var entry = msg.Data as TimeEntryData;
            if (entry == null) {
                return;
            }

            var isExcluded = entry.DeletedAt != null
                             || msg.Action == DataAction.Delete
                             || entry.State == TimeEntryState.New;

            if (isExcluded) {
                RemoveEntry (entry);
            } else {
                AddOrUpdateEntry (new TimeEntryData (entry));
            }
        }

        private void AddOrUpdateEntry (TimeEntryData entry)
        {
            int groupIndex;
            int newIndex;
            int oldIndex = -1;
            NotifyCollectionChangedAction entryAction;

            TimeEntryData existingEntry;
            DateGroup grp;
            bool isNewGroup = false;

            if (FindExistingEntry (entry, out grp, out existingEntry)) {
                if (entry.StartTime != existingEntry.StartTime) {
                    var date = entry.StartTime.ToLocalTime ().Date;
                    oldIndex = GetTimeEntryIndex (existingEntry);
                    if (grp.Date != date) {
                        // Need to move entry:
                        grp.Remove (existingEntry);
                        DispatchCollectionEvent (CollectionEventBuilder.GetEvent (NotifyCollectionChangedAction.Replace, GetDateGroupIndex (grp), -1));

                        grp = GetGroupFor (entry, out isNewGroup);
                        grp.Add (entry);
                        entryAction = NotifyCollectionChangedAction.Move;
                        Sort ();
                    } else {
                        grp.DataObjects.UpdateData (entry);
                        Sort ();
                        newIndex = GetTimeEntryIndex (entry);
                        if (newIndex != oldIndex) {
                            OnUpdated();
                            DispatchCollectionEvent (CollectionEventBuilder.GetEvent (NotifyCollectionChangedAction.Move, newIndex, oldIndex));
                        }
                        entryAction = NotifyCollectionChangedAction.Replace;
                    }
                } else {
                    grp.DataObjects.UpdateData (entry);
                    entryAction = NotifyCollectionChangedAction.Replace;
                }
            } else {
                grp = GetGroupFor (entry, out isNewGroup);
                grp.Add (entry);
                Sort ();
                entryAction = NotifyCollectionChangedAction.Add;
            }

            // Update datasource.
            OnUpdated();

            // Update group.
            groupIndex = GetDateGroupIndex (grp);
            var groupAction = isNewGroup ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Replace;
            DispatchCollectionEvent (CollectionEventBuilder.GetEvent (groupAction, groupIndex, oldIndex));

            // Updated entry.
            newIndex = GetTimeEntryIndex (entry);
            DispatchCollectionEvent (CollectionEventBuilder.GetEvent (entryAction, newIndex, oldIndex));
        }

        private void RemoveEntry (TimeEntryData entry)
        {
            int groupIndex;
            int entryIndex;
            NotifyCollectionChangedAction groupAction = NotifyCollectionChangedAction.Replace;

            DateGroup grp;
            TimeEntryData oldEntry;

            if (FindExistingEntry (entry, out grp, out oldEntry)) {
                entryIndex = GetTimeEntryIndex (oldEntry);
                groupIndex = GetDateGroupIndex (grp);
                grp.Remove (oldEntry);
                if (grp.DataObjects.Count == 0) {
                    dateGroups.Remove (grp);
                    groupAction = NotifyCollectionChangedAction.Remove;
                }

                OnUpdated ();
                DispatchCollectionEvent (CollectionEventBuilder.GetEvent (groupAction, groupIndex, -1));
                DispatchCollectionEvent (CollectionEventBuilder.GetEvent (NotifyCollectionChangedAction.Remove, entryIndex, -1));
            }
        }

        private bool FindExistingEntry (TimeEntryData dataObject, out DateGroup dateGroup, out TimeEntryData existingDataObject)
        {
            foreach (var grp in dateGroups) {
                foreach (var obj in grp.DataObjects) {
                    if (dataObject.Matches (obj)) {
                        dateGroup = grp;
                        existingDataObject = obj;
                        return true;
                    }
                }
            }

            dateGroup = null;
            existingDataObject = null;
            return false;
        }

        private int GetTimeEntryIndex (TimeEntryData dataObject)
        {
            int count = 0;
            foreach (var grp in dateGroups) {
                count++;
                // Iterate by entry list.
                foreach (var obj in grp.DataObjects) {
                    if (dataObject.Matches (obj)) {
                        return count;
                    }
                    count++;
                }
            }
            return -1;
        }

        private int GetDateGroupIndex (DateGroup dateGroup)
        {
            var count = 0;
            foreach (var grp in dateGroups) {
                if (grp.Date == dateGroup.Date) {
                    return count;
                }
                count += grp.DataObjects.Count + 1;
            }
            return -1;
        }

        private DateGroup GetGroupFor (TimeEntryData dataObject, out bool isNewGroup)
        {
            isNewGroup = false;
            var date = dataObject.StartTime.ToLocalTime ().Date;
            var grp = dateGroups.FirstOrDefault (g => g.Date == date);
            if (grp == null) {
                grp = new DateGroup (date);
                dateGroups.Add (grp);
                isNewGroup = true;
            }
            return grp;
        }

        private void Sort ()
        {
            foreach (var grp in dateGroups) {
                grp.Sort ();
            }
            dateGroups.Sort ((a, b) => b.Date.CompareTo (a.Date));
        }

        private void OnSyncFinished (SyncFinishedMessage msg)
        {
            if (reloadScheduled) {
                reloadScheduled = false;
                IsLoading = false;
                Load (true);
            }

            if (subscriptionSyncFinished != null) {
                var bus = ServiceContainer.Resolve<MessageBus> ();
                bus.Unsubscribe (subscriptionSyncFinished);
                subscriptionSyncFinished = null;
            }
        }

        public event EventHandler Updated;

        private void OnUpdated ()
        {
            if (updateMode != UpdateMode.Immediate) {
                return;
            }
            var handler = Updated;
            if (handler != null) {
                handler (this, EventArgs.Empty);
            }
        }

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        private void DispatchCollectionEvent (NotifyCollectionChangedEventArgs args)
        {
            if (updateMode != UpdateMode.Immediate) {
                return;
            }
            var handler = CollectionChanged;
            if (handler != null) {
                handler (this, args);
            }
        }

        private void BeginUpdate ()
        {
            if (updateMode != UpdateMode.Immediate) {
                return;
            }
            lastItemNumber = Count;
            updateMode = UpdateMode.Batch;
        }

        private void EndUpdate ()
        {
            updateMode = UpdateMode.Immediate;
            OnUpdated ();
            DispatchCollectionEvent (CollectionEventBuilder.GetRangeEvent (NotifyCollectionChangedAction.Add, lastItemNumber, Count - lastItemNumber));
        }

        public void Reload ()
        {
            if (IsLoading) {
                return;
            }

            startFrom = Time.UtcNow;
            dateGroups.Clear ();
            HasMore = true;

            var syncManager = ServiceContainer.Resolve<ISyncManager> ();
            if (syncManager.IsRunning) {
                // Fake loading until initial sync has finished
                IsLoading = true;
                OnUpdated ();

                reloadScheduled = true;
                if (subscriptionSyncFinished == null) {
                    var bus = ServiceContainer.Resolve<MessageBus> ();
                    subscriptionSyncFinished = bus.Subscribe<SyncFinishedMessage> (OnSyncFinished);
                }
            } else {
                Load (true);
            }
        }

        public void LoadMore ()
        {
            Load (false);
        }

        private async void Load (bool initialLoad)
        {
            if (IsLoading || !HasMore) {
                return;
            }

            IsLoading = true;
            var client = ServiceContainer.Resolve<ITogglClient> ();
            OnUpdated ();

            try {
                var dataStore = ServiceContainer.Resolve<IDataStore> ();
                var endTime = startFrom;
                var startTime = startFrom = endTime - TimeSpan.FromDays (4);

                bool useLocal = false;

                if (initialLoad) {
                    useLocal = true;
                    startTime = startFrom = endTime - TimeSpan.FromDays (9);
                }

                // Try with latest data from server first:
                if (!useLocal) {
                    const int numDays = 5;
                    try {
                        var minStart = endTime;
                        var jsonEntries = await client.ListTimeEntries (endTime, numDays);

                        BeginUpdate ();
                        var entries = await dataStore.ExecuteInTransactionAsync (ctx =>
                                      jsonEntries.Select (json => json.Import (ctx)).ToList ());

                        // Add entries to list:
                        foreach (var entry in entries) {
                            AddOrUpdateEntry (entry);

                            if (entry.StartTime < minStart) {
                                minStart = entry.StartTime;
                            }
                        }

                        startTime = minStart;
                        HasMore = (endTime.Date - minStart.Date).Days > 0;
                    } catch (Exception exc) {
                        var log = ServiceContainer.Resolve<ILogger> ();
                        if (exc.IsNetworkFailure () || exc is TaskCanceledException) {
                            log.Info (Tag, exc, "Failed to fetch time entries {1} days up to {0}", endTime, numDays);
                        } else {
                            log.Warning (Tag, exc, "Failed to fetch time entries {1} days up to {0}", endTime, numDays);
                        }

                        useLocal = true;
                    }
                }

                // Fall back to local data:
                if (useLocal) {
                    var store = ServiceContainer.Resolve<IDataStore> ();
                    var userId = ServiceContainer.Resolve<AuthManager> ().GetUserId ();

                    var baseQuery = store.Table<TimeEntryData> ()
                                    .OrderBy (r => r.StartTime, false)
                                    .Where (r => r.State != TimeEntryState.New
                                            && r.DeletedAt == null
                                            && r.UserId == userId);
                    var entries = await baseQuery
                                  .QueryAsync (r => r.StartTime <= endTime
                                               && r.StartTime > startTime);

                    BeginUpdate ();
                    foreach (var entry in entries) {
                        AddOrUpdateEntry (entry);
                    }

                    if (!initialLoad) {
                        var count = await baseQuery
                                    .CountAsync (r => r.StartTime <= startTime);
                        HasMore = count > 0;
                    }
                }
            } catch (Exception exc) {
                var log = ServiceContainer.Resolve<ILogger> ();
                log.Error (Tag, exc, "Failed to fetch time entries");
            } finally {
                IsLoading = false;
                EndUpdate ();
            }
        }

        public IEnumerable<DateGroup> DateGroups
        {
            get { return dateGroups; }
        }

        public IEnumerable<object> Data
        {
            get {
                foreach (var grp in dateGroups) {
                    yield return grp;
                    foreach (var data in grp.DataObjects) {
                        yield return data;
                    }
                }
            }
        }

        public int Count
        {
            get {
                var itemsCount = dateGroups.Sum (g => g.DataObjects.Count);
                return dateGroups.Count + itemsCount;
            }
        }

        public event EventHandler OnHasMoreChanged;

        public bool HasMore
        {
            get {
                return hasMore;
            }
            private set {
                hasMore = value;
                if (OnHasMoreChanged != null) {
                    OnHasMoreChanged (this, EventArgs.Empty);
                }
            }
        }

        public event EventHandler OnIsLoadingChanged;

        public bool IsLoading
        {
            get {
                return isLoading;
            }
            private set {
                isLoading = value;
                if (OnIsLoadingChanged != null) {
                    OnIsLoadingChanged (this, EventArgs.Empty);
                }
            }
        }

        public class DateGroup
        {
            private readonly DateTime date;
            private readonly List<TimeEntryData> dataObjects = new List<TimeEntryData> ();

            public DateGroup (DateTime date)
            {
                this.date = date.Date;
            }

            public DateTime Date
            {
                get { return date; }
            }

            public List<TimeEntryData> DataObjects
            {
                get { return dataObjects; }
            }

            public event EventHandler Updated;

            private void OnUpdated ()
            {
                var handler = Updated;
                if (handler != null) {
                    handler (this, EventArgs.Empty);
                }
            }

            public void Add (TimeEntryData dataObject)
            {
                dataObjects.Add (dataObject);
                OnUpdated ();
            }

            public void Remove (TimeEntryData dataObject)
            {
                dataObjects.Remove (dataObject);
                OnUpdated ();
            }

            public void Sort ()
            {
                dataObjects.Sort ((a, b) => b.StartTime.CompareTo (a.StartTime));
                OnUpdated ();
            }
        }

        private enum UpdateMode {
            Immediate,
            Batch,
        }
    }
}
