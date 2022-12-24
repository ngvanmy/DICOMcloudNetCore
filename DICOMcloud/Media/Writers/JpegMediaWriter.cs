﻿using fo = FellowOakDicom;
using DICOMcloud.IO;
using System.IO;
using System.Drawing;
using FellowOakDicom.Imaging;

namespace DICOMcloud.Media
{
    public class JpegMediaWriter : DicomMediaWriterBase
    {
        public JpegMediaWriter ( ) : base ( ) {}
         
        public JpegMediaWriter ( IMediaStorageService mediaStorage, IDicomMediaIdFactory mediaFactory ) : base ( mediaStorage, mediaFactory ) {}

        public override string MediaType
        {
            get
            {
                return MimeMediaTypes.Jpeg ;
            }
        }

        protected override bool StoreMultiFrames
        {
            get
            {
                return true ;
            }
        }


        protected override fo.DicomDataset GetMediaDataset ( fo.DicomDataset data, DicomMediaProperties mediaInfo )
        {
            return base.GetMediaDataset ( data, mediaInfo ) ;
        }

        protected override void Upload 
        ( 
            fo.DicomDataset dicomObject, 
            int frame, 
            IStorageLocation storeLocation, 
            DicomMediaProperties mediaProperties 
        )
        {
            var frameIndex = frame - 1 ;
            var dicomImage = new DicomImage (dicomObject, frameIndex);
            // Todo: Test as .AsSharedBitmap() doesn't seem to exist anymore
            var bitmap = dicomImage.RenderImage(frameIndex).As<Bitmap>();
            var stream = new MemoryStream ( ) ;
            
            bitmap.Save (stream, System.Drawing.Imaging.ImageFormat.Jpeg );
            
            stream.Position = 0 ;

            storeLocation.Upload ( stream, MediaType ) ;
        }
    }
}
