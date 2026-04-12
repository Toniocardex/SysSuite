using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SysSuite.Collections
{
    /// <summary>
    /// Durante <see cref="BeginUpdate"/> non propaga <see cref="INotifyCollectionChanged"/>; a
    /// <see cref="EndUpdate"/> emette un solo <see cref="NotifyCollectionChangedAction.Reset"/> solo se nel batch
    /// ci sono stati Add o Remove (non per il solo Move).
    /// </summary>
    public sealed class BatchingObservableCollection<T> : ObservableCollection<T>
    {
        private int _depth;
        private bool _hadAddOrRemove;

        public void BeginUpdate() => _depth++;

        public void EndUpdate()
        {
            if (_depth <= 0)
                return;
            _depth--;
            if (_depth > 0)
                return;

            bool hadAddOrRemove = _hadAddOrRemove;
            _hadAddOrRemove = false;

            if (hadAddOrRemove)
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_depth > 0)
            {
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                    case NotifyCollectionChangedAction.Remove:
                    case NotifyCollectionChangedAction.Replace:
                    case NotifyCollectionChangedAction.Reset:
                        _hadAddOrRemove = true;
                        return;
                    case NotifyCollectionChangedAction.Move:
                        return;
                }

                return;
            }

            base.OnCollectionChanged(e);
        }
    }
}
