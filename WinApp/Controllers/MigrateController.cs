using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinApp.Controllers
{
    class MigrateController : BaseController
    {
        static CodeGenerator engine;
        public CodeGenerator Engine
        {
            get
            {
                if (engine == null)
                    engine = new CodeGenerator { };
                return engine;
            }
        }
        void CreateMenu()
        {
            var sch = Provider.Current.Schema;
            var tbl = new ActionContext("TABLES");
            var vie = new ActionContext("VIEWS");
            App.User.SideMenu = new ActionContext()
                .Add(tbl)
                .Add(vie);

            foreach (var e in sch.Tables.Values.OrderBy(x => x.Name))
            {
                var act = new ActionContext(e.Name, () => {
                    App.Request("migrate/gen", e);
                });
                switch (e.Type)
                {
                    case "V": vie.Add(act); continue;
                }
                tbl.Add(act);
            }
        }
        public override object Index()
        {
            Provider.Current.Schema.Migrate();
            CreateMenu();
            return RedirectToAction("Gen/Model");
        }
        public object Gen(DataSchema.Table table)
        {
            return View(Engine.Run(table));
        }
        public object Gen(string mode)
        {
            var m = App.User.SideMenu;
            if (m == null || m.Childs == null)
            {
                CreateMenu();
            }
            Engine.Mode = mode;
            return View(Engine.Run(null));
        }
    }
}
