using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models.PostGresModels
{
    public class UserSetting
    {
        [Key]
        public int Id { get; set; }
        public int UserId { get; set; }
        public bool IsWhatsAppEnabled { get; set; } = true;
        public bool IsEmailEnabled { get; set; } = true;
        public string Theme { get; set; } = "light";
        public int? AIId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;


        public virtual User User { get; set; }
        public virtual AI AI { get; set; }
    }
}
