using System;
using System.Collections.Generic;

namespace SubD
{
    public class IndexedProperty<Key, Value>
    {
        IDictionary<Key, Value> BackingStore;
        bool AllowAdds;

        public Value this[Key key]
        {
            get => BackingStore[key];
            set {
                if (!AllowAdds && !BackingStore.ContainsKey(key))
                {
                    throw new ArgumentOutOfRangeException();
                }

                BackingStore[key] = value;
            }
        }

        public IndexedProperty(IDictionary<Key, Value> backing_store, bool allow_adds = true)
        {
            BackingStore = backing_store;
            AllowAdds = allow_adds;
        }
    }
}