﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using fo = FellowOakDicom;

namespace DICOMcloud.Pacs.Commands
{
    public interface IDeleteCommand : IDCloudCommand<DeleteCommandData,DCloudCommandResult>
    {
        
    }
}
