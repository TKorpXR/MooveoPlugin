using System;
using UnityEngine;

namespace GlobalSettings.Core
{
    [Serializable]
    public class ObservableSetting <T>
    {
        [SerializeField] private T _value;

        public event Action<T> OnValueChanged;

        public T Value
        {
            get => _value;
            set
            {
                if (!Equals(value, _value))
                {
                    _value = value;
                    OnValueChanged?.Invoke(value);
                }
            }
        }

        public void Bind(Action<T> listener)
        {
            listener?.Invoke(_value);
            OnValueChanged += listener;
        }
        public void Bind(Func<T> getter, Action<T> setter)
        {
            setter?.Invoke(_value);
            //Debug.Log($"Update: {_value}");
            OnValueChanged += setter;
        }

        public void UnBind(Action<T> listener)
        {
            OnValueChanged -= listener;
        }
        public void ForceNotify()
        {
            OnValueChanged?.Invoke(_value);
        }
    
    }
}