import { readFile } from 'node:fs/promises';
import { existsSync, readdirSync } from 'node:fs';
import path from 'node:path';

const requiredHeaderPattern = /SPDX-License-Identifier:\s*PolyForm-Noncommercial-1\.0\.0/;

const designatedFiles = [
  'SaveNEIN.Server/Services/DataFoundationServices.cs',
  'SaveNEIN.Server/Services/DataProviderContracts.cs',
  'SaveNEIN.Server/Services/JurisdictionProfileService.cs',
  'SaveNEIN.Server/Services/ModelDataIngestionService.cs',
  'SaveNEIN.Server/Services/ModelParameterService.cs',
  'SaveNEIN.Server/Services/ModelRunService.cs',
  'SaveNEIN.Server/Controllers/GravityModelRunsController.cs',
  'SaveNEIN.Server/Controllers/ModelSensitivityController.cs',
  'SaveNEIN.Server/Controllers/ModelValidationController.cs',
  'SaveNEIN.Server/Controllers/ModelRunReportsController.cs',
  'SaveNEIN.Server/Controllers/ModelDataController.cs',
  'SaveNEIN.Server/Controllers/ModelProviderIngestionController.cs',
  'SaveNEIN.Server/Controllers/DevelopmentProgramsController.cs',
  'SaveNEIN.Server/Controllers/CasinoCompetitorsController.cs',
  'SaveNEIN.Server/Controllers/GravityModelConfigurationController.cs',
  'SaveNEIN.Server/Data/Entities/GravityModelEntities.cs',
  'SaveNEIN.Server/Data/Entities/DataFoundationEntities.cs',
  'SaveNEIN.Server/Data/Entities/ExtendedDemandEntities.cs',
  'SaveNEIN.Server/Data/Entities/ImpactAccountingEntities.cs',
  'SaveNEIN.Server/Data/Entities/ModelFoundationEntities.cs',
  'SaveNEIN.Server/Data/Entities/ReportEntities.cs',
  'SaveNEIN.Server/Data/Entities/SensitivityEntities.cs',
  'SaveNEIN.Server/Data/Entities/ValidationEntities.cs',
  'SaveNEIN.Server/Data/ModelFoundationInitializer.cs',
  'scripts/validation/validate-model-foundation-migrations.sh',
  'scripts/validation/run-gravity-model-integration.sh',
  'scripts/validation/run-incumbent-calibration.sh',
  'scripts/validation/run-provider-ingestion.sh',
  'scripts/validation/run-michigan-provider-bundle-ingestion.sh',
  'scripts/validation/GravityModelIntegrationHarness/Program.cs'
];

const designatedDirectories = [
  'SaveNEIN.Server/Services/Gravity',
  'SaveNEIN.Server/Services/Validation',
  'SaveNEIN.Server/Services/Reports'
];

function getFilesFromDir(dirPath) {
  if (!existsSync(dirPath)) return [];
  const files = [];
  const entries = readdirSync(dirPath, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      files.push(...getFilesFromDir(full));
    } else if (entry.isFile() && (entry.name.endsWith('.cs') || entry.name.endsWith('.sql') || entry.name.endsWith('.sh'))) {
      files.push(full);
    }
  }
  return files;
}

function getMigrationFiles() {
  const migrationsDir = path.resolve('docs/migrations');
  if (!existsSync(migrationsDir)) return [];
  const entries = readdirSync(migrationsDir);
  return entries
    .filter(name => /^0(0[6-9]|1[0-7])_.*\.sql$/.test(name))
    .map(name => path.join('docs/migrations', name));
}

const allTargets = new Set();

for (const f of designatedFiles) {
  if (existsSync(path.resolve(f))) {
    allTargets.add(path.normalize(f));
  }
}

for (const d of designatedDirectories) {
  const files = getFilesFromDir(path.resolve(d));
  for (const f of files) {
    allTargets.add(path.relative(process.cwd(), f));
  }
}

for (const m of getMigrationFiles()) {
  allTargets.add(path.normalize(m));
}

const violations = [];

for (const targetPath of allTargets) {
  const absolutePath = path.resolve(targetPath);
  try {
    const content = await readFile(absolutePath, 'utf8');
    if (!requiredHeaderPattern.test(content)) {
      violations.push(`${targetPath}: Missing 'SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0' header.`);
    }
  } catch (err) {
    violations.push(`${targetPath}: Error reading file - ${err.message}`);
  }
}

if (violations.length > 0) {
  console.error('Model licensing guard failed! The following files in the Advanced Economic Modeling Subsystem lack PolyForm Noncommercial 1.0.0 license headers:\n');
  violations.forEach(v => console.error(`  - ${v}`));
  console.error('\nPlease add the SPDX header to all model subsystem files before committing or merging to main.');
  process.exit(1);
}

console.log(`Model licensing guard passed. Verified ${allTargets.size} model files for PolyForm Noncommercial 1.0.0 license headers.`);
