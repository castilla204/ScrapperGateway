using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models.PostGresModels
{
    public class SystemSetting
    {
        [Key]
        public int Id { get; set; }
        public bool IsWhatsAppNotificationEnabled { get; set; } = true;
        public bool IsEmailNotificationEnabled { get; set; } = true;
        public string Theme { get; set; } = "light";
        public int? AIId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual AI AI { get; set; }
    }
}
