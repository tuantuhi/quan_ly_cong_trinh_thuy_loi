using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinApp.Views.Giong
{
    using Vst.Controls;
    class Index : BaseView<DataListViewLayout>
    {
        protected override void RenderCore(ViewContext context)
        {
            base.RenderCore(context);
            context.Title = "Giống cây trồng";
            context.TableColumns = new object[] {
                new TableColumn { Name = "Ten" },
                new TableColumn { Name = "Nguon" },
            };
        }
    }

    class Edit : Add
    {
        protected override void OnReady()
        {
            base.OnReady();
            ShowDeleteAction("Ten");
        }
    }
    class Add : EditView
    {
        protected override void RenderCore(ViewContext context)
        {
            base.RenderCore(context);
            context.Title = "Cập nhật giống cây trồng";
            context.Editors = new object[] {
                new EditorInfo { Name = "Ten", Caption = "Tên giống" },
                new EditorInfo { Name = "Nguon", Caption = "Nguồn gốc" },
            };
        }
    }

}
