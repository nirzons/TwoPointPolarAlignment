using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace NirZonshine.NINA.TwoPointPolarAlignment {

    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {

        public Options() {
            InitializeComponent();
        }
    }
}