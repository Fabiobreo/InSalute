using System;
using System.Web.UI.WebControls;

namespace REST_API.Models
{
    public abstract class FilterModelBase : ICloneable
    {
        public abstract object Clone();
    }
}