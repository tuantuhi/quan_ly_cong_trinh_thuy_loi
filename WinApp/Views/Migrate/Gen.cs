using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using Vst.Controls;

namespace WinApp.Views.Migrate
{
    class Gen : BaseView<Border, CodeGenerator.Block>
    {
        void CreateCodeItem()
        {
            if (Model == null)
            {
                MainView.Child = new Label {
                    Content = "Code generator not found",
                };
                return;
            }

            MainView.Child = new CodeViewer(Model.Title, Model.ToString());
        }
        
        protected override void RenderCore(ViewContext context)
        {            
            CreateCodeItem();
        }
    }
}
