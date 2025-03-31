using DataLayer.Models;
using DataLayer.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServicesLayer
{
    public interface IWebMixerService
    {
        Task<List<AdModel>> Search(SearchRequestDto request);
    }
}