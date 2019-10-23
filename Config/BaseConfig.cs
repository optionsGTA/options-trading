using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using Ecng.Common;

namespace OptionBot.Config {
    public interface IConfiguration<T> : ICloneable<T>, IEquatable<T> {}

    public interface IReadOnlyConfiguration : INotifyPropertyChanged {
        void VerifyConfig(List<string> errors);
    }

    [Serializable]
    [DataContract]
    abstract public class BaseConfig : SuspendableViewModelBase {
        protected static readonly Logger _log = new Logger("config");

        public const decimal EpsilonDecimal = 1e-10m;
        public const double EpsilonDouble = 1e-10;

        protected BaseConfig() {
            // ReSharper disable once DoNotCallOverridableMethodsInConstructor
            SetDefaultValues();
        }

        public virtual void VerifyConfig(List<string> errors) {}
        public void Reset() => SetDefaultValues();
        protected virtual void SetDefaultValues() {}

        [Browsable(false)] public virtual Type SerializerType {get {return GetType();}}

        protected string SerializeBin() {
            using(var ms = new MemoryStream()) {
                (new BinaryFormatter()).Serialize(ms, this);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public static BaseConfig DeserializeBin(string serialized) {
            using(var ms = new MemoryStream(Convert.FromBase64String(serialized)))
                return (BaseConfig)(new BinaryFormatter()).Deserialize(ms);
        }

        protected override void OnDeserializing() {
            base.OnDeserializing();

            SetDefaultValues();
        }
    }

    [Serializable]
    [DataContract]
    abstract public class BaseConfig<T, R> : BaseConfig, IConfiguration<T>
                          where T : BaseConfig<T, R>, R
                          where R : class, IReadOnlyConfiguration {

        public void CopyFrom(T other) { CopyFromImpl(other); }
        protected virtual void CopyFromImpl(T other) { }

        #region IEquatable

        public override int GetHashCode() {
            // ReSharper disable once BaseObjectGetHashCodeCallInGetHashCode
            return base.GetHashCode(); // not necessary for this class
        }

        public override bool Equals(object other) {
            if(ReferenceEquals(other, null)) return false;
            if(ReferenceEquals(other, this)) return true;

            return GetType() == other.GetType() && OnEquals((T)other);
        }

        public bool Equals(T other) {
            if(ReferenceEquals(other, null)) return false;
            if(ReferenceEquals(other, this)) return true;

            return other.GetType() == GetType() && OnEquals(other);
        }

        protected virtual bool OnEquals(T other) {
            return true;
        }

        #endregion

        #region ICloneable

        object ICloneable.Clone() { return Clone(); }

        public virtual T Clone() { return (T)BaseConfig.DeserializeBin(SerializeBin()); }

        #endregion
    }
}
