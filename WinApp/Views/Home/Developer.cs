using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinApp.Views.Home
{
    class Developer : Index
    {
        protected override void ShowComment(string un)
        {
            base.ShowComment(un);
            Add("Các chức năng dưới đỉnh được tạo trong Controller hoặc View.", 14);
        }
    }
}
