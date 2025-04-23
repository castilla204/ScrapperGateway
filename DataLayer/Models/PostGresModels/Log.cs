using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataLayer.Models.PostGresModels
{
    public class Log
    {
        [Key]
        public int Id { get; set; }
        public string LogLevel { get; set; }
        public string Message { get; set; }
        public string? Details { get; set; }
        public int? UserId { get; set; }
        public string? Source { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public virtual User? User { get; set; }

    }
}
