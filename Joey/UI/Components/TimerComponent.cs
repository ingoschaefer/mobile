﻿using System;
using System.ComponentModel;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Toggl.Joey.Data;
using Toggl.Joey.UI.Activities;
using Toggl.Joey.UI.Fragments;
using Toggl.Joey.UI.Utils;
using Toggl.Joey.UI.Views;
using Toggl.Phoebe;
using Toggl.Phoebe.Analytics;
using Toggl.Phoebe.Data;
using Toggl.Phoebe.Data.DataObjects;
using Toggl.Phoebe.Data.Models;
using Toggl.Phoebe.Data.Utils;
using Toggl.Phoebe.Logging;
using XPlatUtils;
using Activity = Android.Support.V4.App.FragmentActivity;
using Fragment = Android.Support.V4.App.Fragment;

namespace Toggl.Joey.UI.Components
{
    public class TimerComponent
    {
        private static readonly string LogTag = "TimerComponent";
        private readonly Handler handler = new Handler ();
        private PropertyChangeTracker propertyTracker;
        private ActiveTimeEntryManager timeEntryManager;
        private TimeEntryModel backingActiveTimeEntry;
        private bool canRebind;
        private bool isProcessingAction;
        private bool hideDuration;
        private bool hideAction;

        protected TextView DurationTextView { get; private set; }

        protected Button ActionButton { get; private set; }

        public View Root { get; private set; }

        private Activity activity;

        private void FindViews ()
        {
            ActionButton = Root.FindViewById<Button> (Resource.Id.ActionButton).SetFont (Font.Roboto);
            DurationTextView = Root.FindViewById<TextView> (Resource.Id.DurationTextView).SetFont (Font.RobotoLight);

            ActionButton.Click += OnActionButtonClicked;
            DurationTextView.Click += OnDurationTextClicked;
        }

        public void OnCreate (Activity activity)
        {
            this.activity = activity;

            propertyTracker = new PropertyChangeTracker ();

            Root = LayoutInflater.From (activity).Inflate (Resource.Layout.TimerComponent, null);

            FindViews ();
        }

        public void OnDestroy (Activity activity)
        {
            if (propertyTracker != null) {
                propertyTracker.Dispose ();
                propertyTracker = null;
            }
        }

        public void OnStart ()
        {
            // Hook up to time entry manager
            if (timeEntryManager == null) {
                timeEntryManager = ServiceContainer.Resolve<ActiveTimeEntryManager> ();
                timeEntryManager.PropertyChanged += OnActiveTimeEntryManagerPropertyChanged;
            }

            canRebind = true;
            SyncModel ();
            Rebind ();
        }

        public void OnStop ()
        {
            canRebind = false;

            if (timeEntryManager != null) {
                timeEntryManager.PropertyChanged -= OnActiveTimeEntryManagerPropertyChanged;
                timeEntryManager = null;
            }
        }

        private void OnActiveTimeEntryManagerPropertyChanged (object sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == ActiveTimeEntryManager.PropertyActive) {
                if (SyncModel ()) {
                    Rebind ();
                }
            }
        }

        private bool SyncModel ()
        {
            var shouldRebind = true;

            var data = ActiveTimeEntryData;
            if (data != null) {
                if (backingActiveTimeEntry == null) {
                    backingActiveTimeEntry = new TimeEntryModel (data);
                } else {
                    backingActiveTimeEntry.Data = data;
                    shouldRebind = false;
                }
            }

            return shouldRebind;
        }

        private TimeEntryData ActiveTimeEntryData
        {
            get {
                if (timeEntryManager == null) {
                    return null;
                }
                return timeEntryManager.Active;
            }
        }

        private TimeEntryModel ActiveTimeEntry
        {
            get {
                if (ActiveTimeEntryData == null) {
                    return null;
                }
                return backingActiveTimeEntry;
            }
        }

        private void ResetTrackedObservables ()
        {
            if (propertyTracker == null) {
                return;
            }

            propertyTracker.MarkAllStale ();

            var model = ActiveTimeEntry;
            if (model != null) {
                propertyTracker.Add (model, HandleTimeEntryPropertyChanged);
            }

            propertyTracker.ClearStale ();
        }

        private void HandleTimeEntryPropertyChanged (string prop)
        {
            if (prop == TimeEntryModel.PropertyState
                    || prop == TimeEntryModel.PropertyStartTime
                    || prop == TimeEntryModel.PropertyStopTime) {
                Rebind ();
            }
        }

        void OnDurationTextClicked (object sender, EventArgs e)
        {
            var currentEntry = ActiveTimeEntry;
            if (currentEntry == null) {
                return;
            }
            new ChangeTimeEntryDurationDialogFragment (currentEntry).Show (activity.SupportFragmentManager, "duration_dialog");
        }

        private void Rebind ()
        {
            ResetTrackedObservables ();

            var currentEntry = ActiveTimeEntry;
            if (!canRebind || currentEntry == null) {
                return;
            }

            var res = activity.Resources;
            if (currentEntry.State == TimeEntryState.New && currentEntry.StopTime.HasValue) {
                // Save button
                ActionButton.Text = res.GetString (Resource.String.TimerSaveButtonText);
                ActionButton.SetBackgroundColor (res.GetColor (Resource.Color.gray));
            } else if (currentEntry.State == TimeEntryState.Running) {
                // Stop button
                ActionButton.Text = res.GetString (Resource.String.TimerStopButtonText);
                ActionButton.SetBackgroundColor (res.GetColor (Resource.Color.bright_red));
            } else {
                // Start button
                ActionButton.Text = res.GetString (Resource.String.TimerStartButtonText);
                ActionButton.SetBackgroundColor (res.GetColor (Resource.Color.bright_green));
            }

            ActionButton.Visibility = HideAction ? ViewStates.Gone : ViewStates.Visible;

            if (currentEntry.State == TimeEntryState.Running && !HideDuration) {
                var duration = currentEntry.GetDuration ();
                DurationTextView.Text = TimeSpan.FromSeconds ((long)duration.TotalSeconds).ToString ();
                DurationTextView.Visibility = ViewStates.Visible;

                // Schedule next rebind:
                handler.RemoveCallbacks (Rebind);
                handler.PostDelayed (Rebind, 1000 - duration.Milliseconds);
            } else {
                DurationTextView.Visibility = ViewStates.Gone;
            }
        }

        public bool HideDuration
        {
            get { return hideDuration; }
            set {
                if (hideDuration != value) {
                    hideDuration = value;
                    Rebind ();
                }
            }
        }

        public bool HideAction
        {
            get { return hideAction; }
            set {
                if (hideAction != value) {
                    hideAction = value;
                    Rebind ();
                }
            }
        }

        private async void OnActionButtonClicked (object sender, EventArgs e)
        {
            // Protect from double clicks
            if (isProcessingAction) {
                return;
            }

            isProcessingAction = true;
            try {
                var entry = ActiveTimeEntry;
                if (entry == null) {
                    return;
                }

                // Make sure that we work on the copy of the entry to not affect the rest of the logic.
                entry = new TimeEntryModel (new TimeEntryData (entry.Data));

                var showProjectSelection = false;

                try {
                    if (entry.State == TimeEntryState.New && entry.StopTime.HasValue) {
                        await entry.StoreAsync ();

                        // Ping analytics
                        ServiceContainer.Resolve<ITracker> ().SendTimerStartEvent (TimerStartSource.AppManual);
                    } else if (entry.State == TimeEntryState.Running) {
                        await entry.StopAsync ();

                        // Ping analytics
                        ServiceContainer.Resolve<ITracker> ().SendTimerStopEvent (TimerStopSource.App);
                    } else {
                        await entry.StartAsync ();

                        // Ping analytics
                        ServiceContainer.Resolve<ITracker> ().SendTimerStartEvent (TimerStartSource.AppNew);
                        OpenTimeEntryEdit (entry);
                    }
                } catch (Exception ex) {
                    var log = ServiceContainer.Resolve<ILogger> ();
                    log.Warning (LogTag, ex, "Failed to change time entry state.");
                }

                var bus = ServiceContainer.Resolve<MessageBus> ();
                bus.Send (new UserTimeEntryStateChangeMessage (this, entry));
            } finally {
                isProcessingAction = false;
            }
        }

        private void OpenTimeEntryEdit (TimeEntryModel model)
        {
            var i = new Intent (activity, typeof (EditTimeEntryActivity));
            i.PutExtra (EditTimeEntryActivity.ExtraTimeEntryId, model.Id.ToString ());
            activity.StartActivity (i);
        }

        private bool ChooseProjectForNew
        {
            get {
                return ServiceContainer.Resolve<SettingsStore> ().ChooseProjectForNew;
            }
        }
    }
}
