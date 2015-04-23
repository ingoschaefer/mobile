﻿using System;
using System.Collections.Specialized;
using System.Linq;
using Android.Support.V7.Widget;
using Android.Views;
using Android.Views.Animations;
using Android.Widget;
using Toggl.Phoebe.Data.Views;

namespace Toggl.Joey.UI.Adapters
{
    public abstract class RecycledDataViewAdapter<T> : RecyclerView.Adapter
    {
        protected static readonly int ViewTypeLoaderPlaceholder = 0;
        protected static readonly int ViewTypeContent = 1;
        private readonly int LoadMoreOffset = 3;
        private CollectionCachingDataView<T> dataView;
        private int lastLoadingPosition;

        protected RecycledDataViewAdapter (ICollectionDataView<T> dataView)
        {
            this.dataView = new CollectionCachingDataView<T> (dataView);
            this.dataView.CollectionChanged += OnCollectionChanged;
            this.dataView.OnIsLoadingChanged += OnLoading;
            this.dataView.OnHasMoreChanged += OnHasMore;
            HasStableIds = false;
        }

        protected override void Dispose (bool disposing)
        {
            if (disposing) {
                if (dataView != null) {
                    var sourceView = dataView.Source as IDisposable;
                    if (sourceView != null) {
                        sourceView.Dispose ();
                    }

                    dataView.Dispose ();
                    dataView = null;
                }
            }
            base.Dispose (disposing);
        }

        private void OnLoading (object sender, EventArgs e)
        {
            // Need to access the Handle property, else mono optimises/loses the context and we get a weird
            // low-level exception about "'jobject' must not be IntPtr.Zero".
            if (Handle == IntPtr.Zero) {
                return;
            }

            // Sometimes a new call to LoadMore is needed.
            if (lastLoadingPosition + LoadMoreOffset > ItemCount && dataView.HasMore && !dataView.IsLoading) {
                dataView.LoadMore();
            }
        }

        private void OnHasMore (object sender, EventArgs e)
        {
            if (Handle == IntPtr.Zero) {
                return;
            }

            if (!dataView.HasMore) {
                NotifyItemRemoved (dataView.Count);
            }
        }

        private void OnCollectionChanged (object sender, NotifyCollectionChangedEventArgs e)
        {
            if (Handle == IntPtr.Zero) {
                return;
            }

            CollectionChanged (e);
        }

        public virtual T GetEntry (int position)
        {
            if (position == dataView.Count && dataView.IsLoading) {
                return default (T);
            }
            return dataView.Data.ElementAt (position);
        }

        public override long GetItemId (int position)
        {
            return -1;
        }

        public override int GetItemViewType (int position)
        {
            if (position == dataView.Count && dataView.IsLoading) {
                return ViewTypeLoaderPlaceholder;
            }
            return ViewTypeContent;
        }

        public override RecyclerView.ViewHolder OnCreateViewHolder (ViewGroup parent, int viewType)
        {
            return viewType == ViewTypeLoaderPlaceholder ? new SpinnerHolder (GetLoadIndicatorView (parent)) : GetViewHolder (parent, viewType);
        }

        public override void OnBindViewHolder (RecyclerView.ViewHolder holder, int position)
        {
            if (position + LoadMoreOffset > ItemCount && dataView.HasMore && !dataView.IsLoading) {
                lastLoadingPosition = position;
                dataView.LoadMore ();
            }

            if (GetItemViewType (position) == ViewTypeLoaderPlaceholder) {
                var spinnerHolder = (SpinnerHolder)holder;
                spinnerHolder.StartAnimation ();
                return;
            }

            BindHolder (holder, position);
        }

        protected abstract RecyclerView.ViewHolder GetViewHolder (ViewGroup parent, int viewType);

        protected abstract void BindHolder (RecyclerView.ViewHolder holder, int position);

        protected abstract void CollectionChanged (NotifyCollectionChangedEventArgs e);

        public override int ItemCount
        {
            get {
                if (dataView.HasMore) {
                    return dataView.Count + 1;
                }
                return dataView.Count;
            }
        }

        public CollectionCachingDataView<T> DataView
        {
            get { return dataView; }
        }

        protected virtual View GetLoadIndicatorView (ViewGroup parent)
        {
            var view = LayoutInflater.FromContext (parent.Context).Inflate (
                           Resource.Layout.TimeEntryListLoadingItem, parent, false);
            return view;
        }

        protected class SpinnerHolder : RecyclerView.ViewHolder
        {
            public ImageView SpinningImage { get; set; }

            public SpinnerHolder (View root) : base (root)
            {
                SpinningImage = ItemView.FindViewById<ImageView> (Resource.Id.LoadingImageView);
                IsRecyclable  = false;
            }

            public virtual void StartAnimation()
            {
                Animation spinningImageAnimation = AnimationUtils.LoadAnimation (ItemView.Context, Resource.Animation.SpinningAnimation);
                SpinningImage.StartAnimation (spinningImageAnimation);
            }
        }
    }
}
