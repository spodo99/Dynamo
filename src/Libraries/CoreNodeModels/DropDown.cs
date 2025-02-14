using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security;
using System.Xml;
using CoreNodeModels.Properties;
using Dynamo.Graph;
using Dynamo.Graph.Nodes;
using Newtonsoft.Json;

namespace CoreNodeModels
{
    /// <summary>
    /// A class used to store a name and associated item for a drop down menu
    /// </summary>
    public class DynamoDropDownItem : IComparable
    {
        public string Name { get; set; }
        public object Item { get; set; }

        public override string ToString()
        {
            return Name;
        }

        public DynamoDropDownItem(string name, object item)
        {
            Name = name;
            Item = item;
        }

        public int CompareTo(object obj)
        {
            if (!(obj is DynamoDropDownItem a))
                return 1;

            return Name.CompareTo(a);
        }

    }

    /// <summary>
    /// Base class for all nodes allowing selection using a drop-down
    /// </summary>
    public abstract class DSDropDownBase : NodeModel
    {
        protected DSDropDownBase()
        {
            ShouldDisplayPreviewCore = false;
        }

        private ObservableCollection<DynamoDropDownItem> items = new ObservableCollection<DynamoDropDownItem>();

        [JsonIgnore]
        public ObservableCollection<DynamoDropDownItem> Items
        {
            get => items;
            set
            {
                items = value;
                RaisePropertyChanged(nameof(Items));
            }
        }

        public override NodeInputData InputData
        {
            //TODO extend cogs graph schema with new
            //`NodeInputTypes.dropdownSelection` support.
            get
            {
                return new NodeInputData
                {
                    Id = this.GUID,
                    Name = this.Name,
                    //because selection makes more sense than defaulting to number...
                    Type = NodeInputTypes.selectionInput,
                    Type2 = NodeInputTypes.dropdownSelection,
                    Description = this.Description,
                    Value = this.SelectedString,
                    SelectedIndex = this.SelectedIndex
                };
            }
        }

        private int selectedIndex = -1;

        /// <summary>
        /// Index of current selection
        /// </summary>
        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                //do not allow selected index to
                //go out of range of the items collection
                if (value > Items.Count - 1 || value < 0)
                {
                    selectedIndex = -1;
                    selectedString = string.Empty;
                }
                else
                {
                    selectedIndex = value;
                    selectedString = GetSelectedStringFromItem(Items.ElementAt(value));
                }

                RaisePropertyChanged("SelectedIndex");
                RaisePropertyChanged("SelectedString");
            }
        }

        private string selectedString = string.Empty;

        /// <summary>
        /// String form of current selected item, so derived class
        /// can save customized data
        /// </summary>
        public string SelectedString
        {
            get => selectedString;
            set
            {
                if (value == selectedString)
                {
                    return;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    var item = Items.FirstOrDefault(i => GetSelectedStringFromItem(i).Equals(value));
                    // In the case that SelectedString deserialize after SelectedIndex
                    // With a valid item from search, get the index of item and replace the current one. 
                    // If no exact match found, fall back to use the default selectedIndex from deserialization.
                    if (item != null)
                    {
                        selectedIndex = Items.IndexOf(item);
                        RaisePropertyChanged(nameof(SelectedIndex));
                    }
                }

                selectedString = value;
                RaisePropertyChanged(nameof(SelectedString));
            }
        }

        protected DSDropDownBase(string outputName)
        {
            OutPorts.Add(new PortModel(PortType.Output, this, new PortData(outputName, string.Format(Resources.DropDownPortDataResultToolTip, outputName))));
            RegisterAllPorts();
            PopulateItems();
        }

        [JsonConstructor]
        protected DSDropDownBase(string outputName, IEnumerable<PortModel> inPorts, IEnumerable<PortModel> outPorts) : base(inPorts, outPorts)
        {
            PopulateItems();
        }

        protected override void SerializeCore(XmlElement nodeElement, SaveContext context)
        {
            base.SerializeCore(nodeElement, context);
            nodeElement.SetAttribute("index", SaveSelectedIndex(SelectedIndex, Items));
        }

        protected override void DeserializeCore(XmlElement nodeElement, SaveContext context)
        {
            base.DeserializeCore(nodeElement, context);
            // Drop downs previously saved their selected index as an int.
            // Between versions of host applications where the number or order of items
            // in a list would vary, this made loading of drop downs un-reliable.
            // We have upgraded drop downs to save their selected index as 
            // something like "9:Reference Point".

            var attrib = nodeElement.Attributes["index"];
            if (attrib == null)
                return;

            selectedIndex = ParseSelectedIndex(attrib.Value, Items);
            if (selectedIndex < 0)
            {
                Warning(Dynamo.Properties.Resources.NothingIsSelectedWarning);
                selectedString = string.Empty;
            }
            else
            {
                selectedString = selectedIndex > Items.Count - 1 ? string.Empty : GetSelectedStringFromItem(Items.ElementAt(selectedIndex));
            }
        }

        protected override bool UpdateValueCore(UpdateValueParams updateValueParams)
        {
            string value = updateValueParams.PropertyValue;

            if (updateValueParams.PropertyName == "Value" && value != null)
            {
                SelectedIndex = ParseSelectedIndex(value, Items);

                if (SelectedIndex < 0)
                {
                    Warning(Dynamo.Properties.Resources.NothingIsSelectedWarning);
                }

                OnNodeModified();

                return true;
            }

            return base.UpdateValueCore(updateValueParams);
        }

        protected virtual int ParseSelectedIndex(string index, IList<DynamoDropDownItem> items)
        {
            return ParseSelectedIndexImpl(index, items);
        }

        public static int ParseSelectedIndexImpl(string index, IList<DynamoDropDownItem> items)
        {
            int selectedIndex = -1;

            var splits = index.Split(':');
            if (splits.Count() > 1)
            {
                var name = XmlUnescape(index.Substring(index.IndexOf(':') + 1));
                var item = items.FirstOrDefault(i => i.Name == name);
                selectedIndex = item != null ?
                    items.IndexOf(item) :
                    -1;
            }
            else
            {
                var tempIndex = Convert.ToInt32(index);
                selectedIndex = tempIndex > (items.Count - 1) ?
                    -1 :
                    tempIndex;
            }

            return selectedIndex;
        }

        protected virtual string SaveSelectedIndex(int index, IList<DynamoDropDownItem> items)
        {
            return SaveSelectedIndexImpl(index, items);
        }

        public static string SaveSelectedIndexImpl(int index, IList<DynamoDropDownItem> items)
        {
            // If nothing is selected or there are no
            // items in the collection, than return -1
            if (index == -1 || items.Count == 0)
            {
                return "-1";
            }

            var item = items[index];
            return string.Format("{0}:{1}", index, XmlEscape(item.Name));
        }

        /// <summary>
        /// Escape string which could contain XML forbidden chars.
        /// </summary>
        /// <param name="unescaped"></param>
        /// <returns></returns>
        protected static string XmlEscape(string unescaped)
        {
            // TODO: This function can be simplified in Dynamo 3.0
            // since it is one line wrapper
            return SecurityElement.Escape(unescaped);
        }

        /// <summary>
        /// Unescape string which could already be escaped before,
        /// if there is no escaped special char, return as it is
        /// if there is special char escaped, restore them.
        /// </summary>
        /// <param name="escaped"></param>
        /// <returns></returns>
        protected static string XmlUnescape(string escaped)
        {
            // TODO: This function can be simplified in Dynamo 3.0
            // since it is one line wrapper
            return System.Web.HttpUtility.HtmlDecode(escaped);
        }

        /// <summary>
        /// This function is to define what dropdown node need to serialize
        /// as SelectedString. Child Class can redefine the pattern.
        /// e.g. Categories node in Revit will override this function.
        /// </summary>
        /// <param name="item">Selected DynamoDropDownItem</param>
        /// <returns>string to serialize as SelectedString or compare with SelectedString</returns>
        protected virtual string GetSelectedStringFromItem(DynamoDropDownItem item)
        {
            return item == null || item.Name == null ? string.Empty : item.Name;
        }

        public void PopulateItems()
        {
            var currentSelection = SelectedString;
            var selectionState = PopulateItemsCore(currentSelection);

            // Restore the selection when selectedIndex is valid
            if (selectionState == SelectionState.Restore && !string.IsNullOrEmpty(currentSelection))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (GetSelectedStringFromItem(items.ElementAt(i)).Equals(currentSelection))
                    {
                        SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        protected enum SelectionState
        {
            /// <summary>
            /// Derived class has determined the best selection. 
            /// Base class will not attempt to select another item.
            /// </summary>
            Done,

            /// <summary>
            /// Derived class could not determine the right selection
            /// and it is left to the base class to restore the previous 
            /// selection if there was one.
            /// </summary>
            Restore
        }

        /// <summary>
        /// Call this method to allow derived classes to populate the drop down 
        /// list items. An existing selection is provided as an argument so that
        /// it can be retained after drop down items are regenerated.
        /// </summary>
        /// <param name="currentSelection">Item text of an existing selected 
        /// drop down item, or string.Empty if there is no existing selection.
        /// </param>
        /// <returns>See SelectionState for more information.</returns>
        protected abstract SelectionState PopulateItemsCore(string currentSelection);

    }
}
