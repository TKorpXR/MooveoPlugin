using System;using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace GlobalSettings.Core
{
    [Serializable]
    public class ObservableList <T>
    {
        [SerializeField] private List<T> _list = new List<T>();

        public event Action OnChanged;
        public event Action<T> OnItemAdded;
        public event Action<T> OnItemRemoved;
        public event Action OnClear;
    
        public ReadOnlyCollection<T> Items => _list.AsReadOnly();
        public List<T> ListSetter
        {
            set 
            {
                _list = value ?? new List<T>();
                OnChanged?.Invoke();
            }
        }

        public void Add(T element)
        {
            _list.Add(element);
            OnItemAdded?.Invoke(element);
            OnChanged?.Invoke();
        }
        public void Remove(T element)
        {
            if (_list.Remove(element))
            {
                OnItemRemoved?.Invoke(element);
                OnChanged?.Invoke();
            }
        }
        public void Clear()
        {
            if (_list.Count > 0)
            {
                _list.Clear();
                OnClear?.Invoke();
                OnChanged?.Invoke();
            }
        }
    
        public void BindItem(Action<T> onAdd = null, Action<T> onRemove = null)
        {
            if (onAdd != null)
            {
                OnItemAdded += onAdd;
                foreach (var item in _list) onAdd(item);
            }

            if (onRemove != null)
            {
                OnItemRemoved += onRemove;
            }
        }
    
        public void BindGlobal(Action onChanged = null, Action onClear = null)
        {
            if (onChanged != null) OnChanged += onChanged;
            if (onClear != null) OnClear += onClear;
        }

        public void UnBindItem(Action<T> onAdd = null, Action<T> onRemove = null)
        {
            if (onAdd != null) OnItemAdded -= onAdd;
            if (onRemove != null) OnItemRemoved -= onRemove;
        }

        public void UnBindGlobal(Action onChanged = null, Action onClear = null)
        {
            if (onChanged != null) OnChanged -= onChanged;
            if (onClear != null) OnClear -= onClear;
        }
        public void ForceNotify()
        {
            OnChanged?.Invoke();
        }
    }
}