﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GraphQL.Attachments
{
    public static class ResponseParser
    {
        public static async Task<QueryResult> ProcessResponse(this HttpResponseMessage response, CancellationToken cancellation = default)
        {
            Guard.AgainstNull(nameof(response), response);
            var queryResult = new QueryResult();
            await ProcessResponse(
                response,
                resultAction: stream => queryResult.ResultStream = stream,
                attachmentAction: attachment => queryResult.Attachments.Add(attachment.Name, attachment),
                cancellation);
            return queryResult;
        }

        public static async Task ProcessResponse(this HttpResponseMessage response, Action<Stream> resultAction, Action<Attachment>? attachmentAction, CancellationToken cancellation = default)
        {
            Guard.AgainstNull(nameof(response), response);
            if (!response.IsMultipart())
            {
                resultAction(await response.Content.ReadAsStreamAsync());
                return;
            }

            var multipart = await response.Content.ReadAsMultipartAsync(cancellation);
            await ProcessBody(multipart, resultAction);

            await foreach (var attachment in ReadAttachments(multipart).WithCancellation(cancellation))
            {
                if (attachmentAction == null)
                {
                    throw new Exception("Found an attachment but handler had no AttachmentAction.");
                }

                attachmentAction(attachment);
            }
        }

        private static async IAsyncEnumerable<Attachment> ReadAttachments(MultipartMemoryStreamProvider multipart)
        {
            foreach (var content in multipart.Contents.Skip(1))
            {
                var name = content.Headers.ContentDisposition.Name;
                var stream = await content.ReadAsStreamAsync();
                 yield return new Attachment
                (
                    name: name,
                    stream: stream,
                    headers: content.Headers
                );
            }
        }

        static async Task ProcessBody(MultipartStreamProvider multipart, Action<Stream> resultAction)
        {
            var first = multipart.Contents.FirstOrDefault();
            if (first == null)
            {
                throw new Exception("Expected the multipart response have at least one part which contains the graphql response data.");
            }

            var name = first.Headers.ContentDisposition.Name;
            if (name != null)
            {
                throw new Exception("Expected the first part in the multipart response to be un-named.");
            }

            var stream = await first.ReadAsStreamAsync();
            resultAction(stream);
        }
    }
}