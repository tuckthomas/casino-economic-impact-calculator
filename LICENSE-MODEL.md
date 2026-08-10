# SaveNEIN Advanced Economic Modeling Subsystem License
## PolyForm Noncommercial License 1.0.0

**Copyright (C) 2026 Save Fort Wayne Contributors & Model Authors**  
**Repository Subsystem:** Sector-Weighted Casino Displacement & Gravity Revenue Engine  

---

### Applied Scope

This license governs all files, source code, data entities, algorithms, API endpoints, calibration logic, and database migrations associated with the Advanced Economic Modeling Subsystem in this repository, including but not limited to:

- `SaveNEIN.Server/Services/Gravity/` (Gravity model engine, sector-weighted displacement calculator, trade area definitions, decay functions)
- `SaveNEIN.Server/Services/Validation/` (Incumbent backtest calibration, sensitivity analysis, evaluation metrics)
- `SaveNEIN.Server/Services/Reports/` (Stored report generators and export formatters)
- `SaveNEIN.Server/Services/DataFoundationServices.cs`, `SaveNEIN.Server/Services/DataProviderContracts.cs`, `SaveNEIN.Server/Services/JurisdictionProfileService.cs`, `SaveNEIN.Server/Services/ModelDataIngestionService.cs`, `SaveNEIN.Server/Services/ModelParameterService.cs`, `SaveNEIN.Server/Services/ModelRunService.cs`
- `SaveNEIN.Server/Controllers/GravityModelRunsController.cs`, `SaveNEIN.Server/Controllers/ModelSensitivityController.cs`, `SaveNEIN.Server/Controllers/ModelValidationController.cs`, `SaveNEIN.Server/Controllers/ModelRunReportsController.cs`, `SaveNEIN.Server/Controllers/ModelDataController.cs`, `SaveNEIN.Server/Controllers/ModelProviderIngestionController.cs`, `SaveNEIN.Server/Controllers/DevelopmentProgramsController.cs`, `SaveNEIN.Server/Controllers/CasinoCompetitorsController.cs`, `SaveNEIN.Server/Controllers/GravityModelConfigurationController.cs`
- `SaveNEIN.Server/Data/Entities/GravityModelEntities.cs`, `SaveNEIN.Server/Data/Entities/DataFoundationEntities.cs`, `SaveNEIN.Server/Data/Entities/ExtendedDemandEntities.cs`, `SaveNEIN.Server/Data/Entities/ImpactAccountingEntities.cs`, `SaveNEIN.Server/Data/Entities/ModelFoundationEntities.cs`, `SaveNEIN.Server/Data/Entities/ReportEntities.cs`, `SaveNEIN.Server/Data/Entities/SensitivityEntities.cs`, `SaveNEIN.Server/Data/Entities/ValidationEntities.cs`, `SaveNEIN.Server/Data/ModelFoundationInitializer.cs`
- `docs/migrations/006_gravity_model_foundation.sql` through `docs/migrations/017_nullable_facility_evidence_flags.sql`
- `scripts/validation/GravityModelIntegrationHarness/` and related automated validation runners

---

### PolyForm Noncommercial License 1.0.0 Text

*Official Reference: <https://polyformproject.org/licenses/noncommercial/1.0.0/>*

#### Acceptance
In order to receive this license, you must agree to its rules. The rules of this license are conditions to the license grants. If you do not agree to such rules, you may not exercise any of the grant rights.

#### Copyright Grant
The licensor grants you a copyright license to make, have made, use, sell, offer for sale, import, and distribute the software for Noncommercial Purposes only.

#### Patent Grant
The licensor grants you a patent license to make, have made, use, sell, offer for sale, import, and distribute the software for Noncommercial Purposes only. The patent license covers all patent claims licensor can license, or has the right to license, that are infringed by the software as given to you by licensor.

#### Noncommercial Purposes
Noncommercial Purposes are uses that are not primarily intended for or directed towards commercial advantage or monetary compensation.

Commercial advantage or monetary compensation includes, without limitation:
1. Charging for access to or use of the software, model endpoints, or calculation outputs.
2. Charging for services, consulting, support, hosting, or maintenance related to the software or economic projections.
3. Using the software to process data or perform economic evaluations for a fee or other commercial consideration.
4. Using the software as part of a commercial product, service, or proprietary offering provided for a fee.
5. Offering, distributing, or deploying the software as part of a commercial SaaS, cloud, API, or managed hosting service.

*For clarity, educational, academic, civic research, personal non-profit advocacy, and non-commercial public interest analysis are Noncommercial Purposes under this license.*

#### Commercial License
If you wish to use the software or any portion of the Advanced Economic Modeling Subsystem for purposes other than Noncommercial Purposes, you must obtain a separate written commercial license from the licensor/authors.

#### Notices
You must ensure that any copy of the software or modified subsystem that you make or distribute includes a copy of this license and all copyright notices contained in the software.

#### No Other Rights
Any rights not expressly granted to you under this license are reserved by the licensor.

#### Fair Use
This license does not reduce, limit, or restrict any rights arising from fair use, fair dealing, or other limitations on exclusive rights under copyright law.

#### No Warranty
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE, AND NONINFRINGEMENT. IN NO EVENT SHALL THE LICENSOR OR AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES, OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT, OR OTHERWISE, ARISING FROM, OUT OF, OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
