﻿using DICOMcloud.DataAccess;
using DICOMcloud.Media;
using DICOMcloud.Pacs;
using DICOMcloud.Wado.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using DICOMcloud.IO;
using FellowOakDicom;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using DICOMcloud.Wado.Configs;

namespace DICOMcloud.Wado
{
    public class QidoRsService : IQidoRsService
    {
        //TODO: move this to a global config class
        public const string MaximumResultsLimit_ConfigName = "qido:maximumResultsLimit" ;
        private readonly IOptions<QidoOptions> _options;
        public const string Instance_Header_Name = "X-Dicom-Instance" ;
        protected IObjectArchieveQueryService QueryService { get; set; }
        protected IDicomMediaIdFactory MediaIdFactory { get; set; }
        protected IMediaStorageService StorageService { get; set; }

        private readonly IHttpContextAccessor _httpcontextaccessor;

        public QidoRsService ( IObjectArchieveQueryService queryService, IHttpContextAccessor httpcontextaccessor, IOptions<QidoOptions> options ): this (queryService, null, null, httpcontextaccessor, options) {}

        public QidoRsService ( IObjectArchieveQueryService queryService, IDicomMediaIdFactory mediaIdFactory, IMediaStorageService storageService, IHttpContextAccessor httpcontextaccessor, IOptions<QidoOptions> options )
        {
            this._options = options ?? throw new ArgumentException(nameof(options));
            this._httpcontextaccessor = httpcontextaccessor;
            QueryService   = queryService ;
            MediaIdFactory = mediaIdFactory;
            StorageService = storageService ;
        }

        public virtual HttpResponseMessage SearchForStudies
        (
            IQidoRequestModel request
        )
        {
            return SearchForDicomEntity ( request, 
            DefaultDicomQueryElements.GetDefaultStudyQuery(),
            delegate
            ( 
                IObjectArchieveQueryService queryService, 
                DicomDataset dicomRequest, 
                IQidoRequestModel qidoRequest 
            )
            {
                IQueryOptions queryOptions = GetQueryOptions ( qidoRequest ) ;

                return queryService.FindStudiesPaged ( dicomRequest, queryOptions ) ;
            }  ) ;
        }

        public virtual HttpResponseMessage SearchForSeries(IQidoRequestModel request)
        {
            return SearchForDicomEntity ( request, 
            DefaultDicomQueryElements.GetDefaultSeriesQuery ( ),
            delegate 
            ( 
                IObjectArchieveQueryService queryService, 
                DicomDataset dicomRequest, 
                IQidoRequestModel qidoResult
            )
            {
                return queryService.FindSeriesPaged ( dicomRequest, GetQueryOptions ( qidoResult ) ) ;
            }  ) ;
        }

        public virtual HttpResponseMessage SearchForInstances(IQidoRequestModel request)
        {
            return SearchForDicomEntity ( request,
            DefaultDicomQueryElements.GetDefaultInstanceQuery ( ),
            delegate 
            ( 
                IObjectArchieveQueryService queryService, 
                DicomDataset dicomRequest, 
                IQidoRequestModel qidoResult
            )
            {
                return queryService.FindObjectInstancesPaged ( dicomRequest, GetQueryOptions ( qidoResult ) ) ;
            }  ) ;
        }

        protected virtual IQueryOptions CreateNewQueryOptions ( ) 
        {
            return new QueryOptions ( ) ;
        }

        protected virtual IQueryOptions GetQueryOptions ( IQidoRequestModel qidoRequest )
        {
            var queryOptions = CreateNewQueryOptions ( ) ;
            
            queryOptions.Limit = Math.Min ( this._options.Value.MaximumResultsLimit, qidoRequest.Limit.HasValue ? qidoRequest.Limit.Value : this._options.Value.MaximumResultsLimit ) ;
            queryOptions.Offset = Math.Max ( 0, qidoRequest.Offset.HasValue ? qidoRequest.Offset.Value : 0 ) ;
            
            return queryOptions ;
        }
        
        private HttpResponseMessage SearchForDicomEntity 
        ( 
            IQidoRequestModel request, 
            DicomDataset dicomSource,
            DoQueryDelegate doQuery 
        )
        {
            if ( null != request.Query )
            {
                HttpResponseMessage response = null;
                var matchingParams = request.Query.MatchingElements;
                var includeParams = request.Query.IncludeElements;

                foreach (var returnParam in includeParams)
                {
                    InsertDicomElement(dicomSource, returnParam, "");
                }

                foreach (var queryParam in matchingParams)
                {
                    string paramValue = queryParam.Value;


                    InsertDicomElement(dicomSource, queryParam.Key, paramValue);
                }

                var results = doQuery(QueryService, dicomSource, request); //TODO: move configuration params into their own object

                if (MultipartResponseHelper.IsMultiPartRequest(request))
                {
                    response = CreateMultipartResponse(request, results.Result);
                }
                else
                {
                    response = CreateJsonResponse(results.Result);
                }

                AddResponseHeaders(request, dicomSource, response, results);

                return response;
            }

            return null;
        }

        private void AddResponseHeaders
        (            
            IQidoRequestModel request, 
            DicomDataset dicomSource,
            HttpResponseMessage response, 
            PagedResult<DicomDataset> results
        )
        {
            //special parameters, if included, a representative instance UID (first) will be returned in header for each DS result
            if (response.IsSuccessStatusCode && request.Query.CustomParameters.ContainsKey("_instance-header"))
            {
                AddPreviewInstanceHeader(results.Result, response);
            }


            AddPaginationHeders(request, response, results);

        }

        private void AddPaginationHeders(IQidoRequestModel request, HttpResponseMessage response, PagedResult<DicomDataset> results)
        {
            LinkHeaderBuilder headerBuilder = new LinkHeaderBuilder ( ) ;



            //Todo : Test
            response.Headers.Add ( "link", 
                                    headerBuilder.GetLinkHeader ( results, this._httpcontextaccessor.HttpContext.Request.ReturnAbsolutePath())) ;
            
            response.Headers.Add ( "X-Total-Count", results.TotalCount.ToString() ) ;

            response.Headers.Add ("Access-Control-Expose-Headers", "link" ) ;
            response.Headers.Add ("Access-Control-Allow-Headers", "link" ) ;
            response.Headers.Add ("Access-Control-Expose-Headers", "X-Total-Count" ) ;
            response.Headers.Add ("Access-Control-Allow-Headers", "X-Total-Count" ) ;

            if ( results.TotalCount > results.Result.Count() )
            {
                if ( !request.Limit.HasValue || 
                     (request.Limit.HasValue && request.Limit.Value > results.PageSize ) )
                {
                    response.Headers.Add ("Warning", "299 " + "DICOMcloud" + 
                    "  \"The number of results exceeded the maximum supported by the server. Additional results can be requested.\"" ) ;
                    
                    //DICOM: http://dicom.nema.org/dicom/2013/output/chtml/part18/sect_6.7.html
                    //Warning: 299 {SERVICE}: "The number of results exceeded the maximum supported by the server. Additional results can be requested.
                }
            }
        }

        private static HttpResponseMessage CreateJsonResponse(IEnumerable<DicomDataset> results)
        {
            HttpResponseMessage response;
            StringBuilder jsonReturn = new StringBuilder ( "[" ) ;

            JsonDicomConverter converter = new JsonDicomConverter ( ) { IncludeEmptyElements = true } ;
            int count = 0 ;


            foreach ( var dsResponse in results )
            {
                count++ ;

                jsonReturn.AppendLine (converter.Convert ( dsResponse )) ;

                jsonReturn.Append(",") ;
            }

            if ( count > 0 )
            {
                jsonReturn.Remove ( jsonReturn.Length -1, 1 ) ;
            }

            jsonReturn.Append("]") ;
                
            response = new HttpResponseMessage (System.Net.HttpStatusCode.OK )  { 
                                                Content = new StringContent ( jsonReturn.ToString ( ), 
                                                Encoding.UTF8, 
                                                MimeMediaTypes.Json) } ;    
            return response;
        }

        private static HttpResponseMessage CreateMultipartResponse(IQidoRequestModel request, IEnumerable<DicomDataset> results)
        {
            HttpResponseMessage response;
            
            
            if ( MultipartResponseHelper.GetSubMediaType ( request.AcceptHeader.FirstOrDefault ( ) ) == MimeMediaTypes.xmlDicom )
            {
                MultipartContent multiContent ;
                        

                response        = new HttpResponseMessage ( ) ;
                multiContent    = new MultipartContent ( "related", MultipartResponseHelper.DicomDataBoundary ) ;           
                        
                response.Content = multiContent ;

                foreach ( var result in results)
                {
                    XmlDicomConverter converter = new XmlDicomConverter ( ) ;

                    MultipartResponseHelper.AddMultipartContent ( multiContent, 
                                                                    new WadoResponse ( new MemoryStream ( Encoding.ASCII.GetBytes ( converter.Convert (result) )), 
                                                                                        MimeMediaTypes.xmlDicom ) ) ;
                }

                multiContent.Headers.ContentType.Parameters.Add ( new System.Net.Http.Headers.NameValueHeaderValue ( "type", 
                                                                                            "\"" + MimeMediaTypes.xmlDicom + "\"" ) ) ;
            }
            else
            {
                response = new HttpResponseMessage ( System.Net.HttpStatusCode.BadRequest ) ;
            }

            return response;
        }

        private void AddPreviewInstanceHeader ( IEnumerable<DicomDataset> results, HttpResponseMessage response ) 
        {
            response.Headers.Add ("Access-Control-Expose-Headers", Instance_Header_Name ) ;
            
            foreach ( var result in results ) 
            {
                var queryDs = DefaultDicomQueryElements.GetDefaultInstanceQuery();
                string studyUid  = result.GetSingleValueOrDefault<string> ( DicomTag.StudyInstanceUID, "" ) ;
                string seriesUid = result.GetSingleValueOrDefault<string> ( DicomTag.SeriesInstanceUID, "" ) ;
                var queryOptions = CreateNewQueryOptions ( ) ;
            
                queryDs.AddOrUpdate ( DicomTag.StudyInstanceUID, studyUid );
                queryDs.AddOrUpdate ( DicomTag.SeriesInstanceUID, seriesUid );
                queryOptions.Limit  = 1 ;
                queryOptions.Offset = 0 ;
            
                var instances = QueryService.FindObjectInstances (queryDs, queryOptions) ;
                string queryStudyUid = null, querySeriesUid = null, queryInstanceUid = null ;

                foreach  ( var instance in instances )
                {
                    queryStudyUid = instance.GetSingleValueOrDefault<string> ( DicomTag.StudyInstanceUID, "" ) ;
                    querySeriesUid = instance.GetSingleValueOrDefault<string> ( DicomTag.SeriesInstanceUID, "" ) ;
                    queryInstanceUid = instance.GetSingleValueOrDefault<string> ( DicomTag.SOPInstanceUID, "" ) ;

                    break ;
                }

                if ( string.IsNullOrWhiteSpace (queryStudyUid) || string.IsNullOrWhiteSpace ( querySeriesUid) || string.IsNullOrWhiteSpace(queryInstanceUid))
                {
                    response.Headers.Add (Instance_Header_Name, "" ) ;
                }
                else
                {
                    response.Headers.Add (Instance_Header_Name, string.Format ( "{0}:{1}:{2}", queryStudyUid, querySeriesUid, queryInstanceUid) ) ;
                }
            }
        }

        private void InsertDicomElement(DicomDataset dicomRequest, string paramKey, string paramValue)
        {
            List<string> elements = new List<string>();

            elements.AddRange(paramKey.Split('.'));

            if(elements.Count > 1)
            {
                CreateSequence(elements, 0, dicomRequest, paramValue);
            }
            else
            {
                CreateElement(elements[0], dicomRequest, paramValue);
            }
        }

        private void CreateElement(string tagString, DicomDataset dicomRequest, string value)
        {
            // special include fields. Server include all by default.
            if (tagString.ToLower() == "all")
            {
                return;
            }

            uint tag = GetTagValue (tagString);

            InsertQidoValueAsDicom (tag, dicomRequest, value);
        }

        private void CreateSequence(List<string> elements, int currentElementIndex, DicomDataset dicomRequest, string value)
        {
            uint tag = GetTagValue ( elements[currentElementIndex] ) ;
            var dicEntry = DicomDictionary.Default[tag] ;
            DicomSequence sequence ;
            DicomDataset  item ;
            
            dicomRequest.AddOrUpdate ( new DicomSequence ( dicEntry.Tag ) ) ;
            sequence = dicomRequest.GetSequence(dicEntry.Tag);

            item = new DicomDataset ( ).NotValidated();

            sequence.Items.Add ( item ) ;
            
            
            for ( int index = (currentElementIndex+1); index < elements.Count; index++  )
            {
                tag = GetTagValue ( elements[index] ) ;
                
                dicEntry = DicomDictionary.Default[tag] ;

                if (  dicEntry.ValueRepresentations.Contains (DicomVR.SQ) )
                {
                    CreateSequence ( elements, index, item, value) ;

                    break ;
                }
                else
                {
                    InsertQidoValueAsDicom (tag, item, value);
                }
            }
        }

        private static uint GetTagValue (string tagString)
        {
            uint tag ;


            if ( !Char.IsDigit (tagString[0]))
            {
                var element = FellowOakDicom.DicomDictionary.Default.Where ( n=>n.Keyword.ToLower( ) == tagString.ToLower()).FirstOrDefault ( ) ;

                if ( null == element )
                {
                    throw new DICOMcloud.DCloudException ( "Invalid matching parameter: " + tagString ) ;
                }

                tag = (uint) element.Tag.DictionaryEntry.Tag; 
            }
            else
            {
                tag = uint.Parse (tagString, System.Globalization.NumberStyles.HexNumber) ;
            }

            return tag;
        }

        private void InsertQidoValueAsDicom (uint tag, DicomDataset dicomRequest, string value)
        {
            DicomTag dTag = tag;

            if (dTag.DictionaryEntry.ValueRepresentations.Contains ( DicomVR.UI))
            { 
                var values = value.Split (',');

                dicomRequest.AddOrUpdate(tag, values);
            }

            dicomRequest.AddOrUpdate(tag, value);
        }

        private delegate PagedResult<DicomDataset> DoQueryDelegate 
        ( 
            IObjectArchieveQueryService queryService, 
            DicomDataset dicomRequest, 
            IQidoRequestModel request
        ) ;
    }
}
