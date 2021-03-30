﻿// Copyright 2021 Google Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Tests creating a backup using customer managed encryption.
/// </summary>
[Collection(nameof(SpannerFixture))]
public class CreateBackupWithEncryptionKeyAsyncTest
{
    private readonly bool _runCmekBackupSampleTests;
    private readonly SpannerFixture _fixture;

    public CreateBackupWithEncryptionKeyAsyncTest(SpannerFixture fixture)
    {
        _fixture = fixture;
        bool.TryParse(Environment.GetEnvironmentVariable("RUN_SPANNER_CMEK_BACKUP_SAMPLES_TESTS"), out _runCmekBackupSampleTests);
    }

    [SkippableFact]
    public async Task TestCreatBackupWithEncryptionKeyAsync()
    {
        Skip.If(!_runCmekBackupSampleTests, "Spanner CMEK backup sample tests are disabled by default for performance reasons. Set the environment variable RUN_SPANNER_CMEK_BACKUP_SAMPLES_TESTS=true to enable the test.");
        // Create a backup with a custom encryption key.
        var sample = new CreateBackupWithEncryptionKeyAsyncSample();
        var backup = await sample.CreateBackupWithEncryptionKeyAsync(_fixture.ProjectId, _fixture.InstanceId, _fixture.FixedEncryptedDatabaseId, _fixture.EncryptedBackupId, _fixture.KmsKeyName);
        Assert.Equal(_fixture.KmsKeyName.CryptoKeyId, backup.EncryptionInfo.KmsKeyVersionAsCryptoKeyVersionName.CryptoKeyId);
    }
}
