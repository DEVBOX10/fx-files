﻿using Functionland.FxFiles.Shared.Models;
using Microsoft.VisualBasic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Functionland.FxFiles.Shared.TestInfra.Implementations
{
    public abstract partial class FileServicePlatformTest : PlatformTest
    {
        protected abstract string OnGetTestsRootPath();
        protected abstract IFileService OnGetFileService();

        protected async Task OnRunFileServiceTestAsync(IFileService fileService, string rootPath)
        {

            FsArtifact? testsRootArtifact = null;
            try
            {
                try
                {
                    testsRootArtifact = await fileService.CreateFolderAsync(rootPath, "FileServiceTestsFolder");
                }
                catch (DomainLogicException ex) when (ex.Message == "The folder already exists exception") //TODO: use AppStrings for exception
                {
                    var rootArtifacts = await GetArtifactsAsync(fileService, rootPath);
                    testsRootArtifact = rootArtifacts.FirstOrDefault(rootArtifact => rootArtifact.FullPath == Path.Combine(rootPath, "FileServiceTestsFolder"));
                }

                var testRootArtifact = await fileService.CreateFolderAsync(testsRootArtifact.FullPath!, $"TestRun-{DateTimeOffset.Now:yyyyMMddHH-mmssFFF}");
                var testRoot = testRootArtifact.FullPath!;


                var artifacts = await GetArtifactsAsync(fileService, testRoot);

                Assert.AreEqual(0, artifacts.Count, "new folder must be empty");

                await fileService.CreateFolderAsync(testRoot, "Folder 1");
                var folder11 = await fileService.CreateFolderAsync(Path.Combine(testRoot, "Folder 1"), "Folder 11");
                var file1 = await fileService.CreateFileAsync(Path.Combine(testRoot, "file1.txt"), GetSampleFileStream());
                var file11 = await fileService.CreateFileAsync(Path.Combine(testRoot, "Folder 1/file11.txt"), GetSampleFileStream());               

                artifacts = await GetArtifactsAsync(fileService, testRoot);
                Assert.AreEqual(2, artifacts.Count, "Create folder and file in root");

                artifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 1"));
                Assert.AreEqual(2, artifacts.Count, "Create folder and file in sub directory");


                //Expecting exceptions
                await Assert.ShouldThrowAsync<DomainLogicException>(async () =>
                {
                    await fileService.CreateFolderAsync(testRoot, "Folder 1");
                }, "The folder already exists exception");

                await Assert.ShouldThrowAsync<DomainLogicException>(async () =>
                {
                    await fileService.CreateFileAsync(Path.Combine(testRoot, "file1.txt"), GetSampleFileStream());
                }, "The file already exists exception");

                //Invalid chars are not the same on both android and windows! No point testing it here.
                //await Assert.ShouldThrowAsync<DomainLogicException>(async () =>
                //{
                //    await fileService.CreateFolderAsync(testRoot, "Folder **");
                //}, "The folder name has invalid chars.");

                await Assert.ShouldThrowAsync<DomainLogicException>(async () =>
                {
                    await fileService.CreateFolderAsync(testRoot, "");
                }, "The folder name is null");

                await Assert.ShouldThrowAsync<DomainLogicException>(async () =>
                {
                    await fileService.CreateFileAsync(Path.Combine(testRoot, ".txt"), GetSampleFileStream());
                }, "The file name is null");
                

                //1. move a file
                var movingFiles = new[] { file1 };
                await fileService.CreateFolderAsync(testRoot, "Folder 2");

                artifacts = await GetArtifactsAsync(fileService, testRoot);
                Assert.AreEqual(3, artifacts.Count, "Before moveing operation.");

                await fileService.MoveArtifactsAsync(movingFiles, Path.Combine(testRoot, "Folder 2"));
                artifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 2"));
                Assert.AreEqual(1, artifacts.Count, "Move a file to a folder. Created on destination");

                artifacts = await GetArtifactsAsync(fileService, testRoot);
                Assert.AreEqual(2, artifacts.Count, "Move a file to a folder. Removed from source");
                artifacts.Clear();


                //2. Move a folder
                var folder12 = await fileService.CreateFolderAsync(Path.Combine(testRoot, "Folder 1"), "Folder 12");
                var movingFolders = new[] { folder12 };

                var srcArtifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 1"));
                Assert.AreEqual(3, srcArtifacts.Count, "Create a file in source, in order to move.");

                await fileService.MoveArtifactsAsync(movingFolders, testRoot);
                srcArtifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 1"));
                Assert.AreEqual(2, srcArtifacts.Count, "Folder removed from source");

                var desArtifacts = await GetArtifactsAsync(fileService, testRoot);
                Assert.AreEqual(3, desArtifacts.Count, "Folder moved to destination");


                //3. Move multiple folders and files
                var folder3 = await fileService.CreateFolderAsync(testRoot, "Folder 3");

                var file31 = await fileService.CreateFileAsync(Path.Combine(folder3.FullPath, "file31.txt"), GetSampleFileStream());
                var file32 = await fileService.CreateFileAsync(Path.Combine(folder3.FullPath, "file32.txt"), GetSampleFileStream());
                var folder31 = await fileService.CreateFolderAsync(folder3.FullPath, "Folder 31");

                var movingItems = new[] { file31, file32, folder31 };

                await fileService.MoveArtifactsAsync(movingItems, testRoot);
                srcArtifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 3"));
                Assert.AreEqual(0, srcArtifacts.Count, "Move folders & files. All removed from source.");

                desArtifacts = await GetArtifactsAsync(fileService, testRoot);
                Assert.AreEqual(7, desArtifacts.Count, "Move folders & files. All moved to destination");


                //4. Move combined items: files and a folder which contains multiple files
                var file311 = await fileService.CreateFileAsync(Path.Combine(testRoot, "Folder 31/file311.txt"), GetSampleFileStream());
                var file312 = await fileService.CreateFileAsync(Path.Combine(testRoot, "Folder 31/file312.txt"), GetSampleFileStream());
                artifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 31"));
                Assert.AreEqual(2, artifacts.Count, "Create files in a folder which is about to move.");

                Array.Clear(movingItems);
                artifacts = await GetArtifactsAsync(fileService, testRoot);

                file31 = artifacts.Where(f => f.Name == "file31.txt").FirstOrDefault();
                Assert.IsNotNull(file32, "File exists in source, before move.");

                file32 = artifacts.Where(f => f.Name == "file32.txt").FirstOrDefault();
                Assert.IsNotNull(file32, "File exists in source, before move.");

                folder31 = artifacts.Where(f => f.Name == "Folder 31").FirstOrDefault();
                Assert.IsNotNull(file32, "Folder exists in source, before move.");

                //Nullability of these items have already been checked in lines 136, 140, 143. No worries then.
                movingItems = new[] { file31, file32, folder31 };

                await fileService.MoveArtifactsAsync(movingItems, Path.Combine(testRoot, "Folder 2"));
                srcArtifacts = await GetArtifactsAsync(fileService, testRoot);
                Assert.AreEqual(4, srcArtifacts.Count, "Move Files & folders. All removed from source.");
                desArtifacts = await GetArtifactsAsync(fileService, Path.Combine(testRoot, "Folder 2"));
                Assert.AreEqual(4, desArtifacts.Count, "Move Files & folders. All moved to destination.");
 

                Assert.Success("Test passed!");
            }
            catch (Exception ex)
            {
                Assert.Fail("Test failed", ex.Message);
            }
        }

        private static async Task<List<FsArtifact>> GetArtifactsAsync(IFileService fileService, string testRoot)
    {
        List<FsArtifact> emptyRootFolderArtifacts = new();
        await foreach (var item in fileService.GetArtifactsAsync(testRoot))
        {
            emptyRootFolderArtifacts.Add(item);
        }

        return emptyRootFolderArtifacts;
    }
        private Stream GetSampleFileStream()
    {
        var sampleText = "Hello streamer!";
        byte[] byteArray = Encoding.ASCII.GetBytes(sampleText);
        MemoryStream stream = new MemoryStream(byteArray);
        return stream;
    }

        protected override async Task OnRunAsync()
    {
        var root = OnGetTestsRootPath();
        var fileService = OnGetFileService();
        await OnRunFileServiceTestAsync(fileService, root);
    }
    }
}
