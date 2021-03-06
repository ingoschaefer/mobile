using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Toggl.Phoebe.Data.Views
{
    public interface ICollectionDataView<T> : INotifyCollectionChanged
    {
        IEnumerable<T> Data { get; }

        int Count { get; }

        bool HasMore { get; }

        void Reload ();

        void LoadMore ();

        bool IsLoading { get; }

        event EventHandler Updated;

        event EventHandler OnIsLoadingChanged;

        event EventHandler OnHasMoreChanged;
    }
}