-- 011_comprehensive_impact_accounting.sql
-- Immutable geographic, displacement, employment, fiscal, social-cost, and net-impact bridges.

CREATE TABLE IF NOT EXISTS model_run_geographic_accounting (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    scope_kind VARCHAR(40) NOT NULL,
    scope_code VARCHAR(160) NOT NULL,
    local_origin_count INTEGER NOT NULL,
    host_jurisdiction_cannibalization NUMERIC(20, 2) NOT NULL,
    cross_jurisdiction_capture NUMERIC(20, 2) NOT NULL,
    outside_or_unmodeled_leakage_capture NUMERIC(20, 2) NOT NULL,
    induced_resident_ggr NUMERIC(20, 2) NOT NULL,
    tourism_ggr NUMERIC(20, 2) NOT NULL,
    traffic_ggr NUMERIC(20, 2) NOT NULL,
    transfer_effect_ggr NUMERIC(20, 2) NOT NULL,
    market_expansion_and_import_ggr NUMERIC(20, 2) NOT NULL,
    stabilized_ggr NUMERIC(20, 2) NOT NULL,
    local_resident_gaming_base NUMERIC(20, 2) NOT NULL,
    excluded_local_casino_cannibalization NUMERIC(20, 2) NOT NULL,
    excluded_repatriated_or_leaked_resident_ggr NUMERIC(20, 2) NOT NULL,
    remaining_local_resident_gaming_base NUMERIC(20, 2) NOT NULL,
    local_origin_ids_json JSONB NOT NULL DEFAULT '[]'::jsonb,
    UNIQUE (model_run_id, scope_kind, scope_code),
    CHECK (local_origin_count >= 0),
    CHECK (host_jurisdiction_cannibalization >= 0),
    CHECK (cross_jurisdiction_capture >= 0),
    CHECK (outside_or_unmodeled_leakage_capture >= 0),
    CHECK (induced_resident_ggr >= 0),
    CHECK (tourism_ggr >= 0),
    CHECK (traffic_ggr >= 0),
    CHECK (transfer_effect_ggr >= 0),
    CHECK (market_expansion_and_import_ggr >= 0),
    CHECK (stabilized_ggr >= 0),
    CHECK (local_resident_gaming_base >= 0),
    CHECK (excluded_local_casino_cannibalization >= 0),
    CHECK (excluded_repatriated_or_leaked_resident_ggr >= 0),
    CHECK (remaining_local_resident_gaming_base >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_sector_displacement (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    scope_kind VARCHAR(40) NOT NULL,
    scope_code VARCHAR(160) NOT NULL,
    sector_key VARCHAR(100) NOT NULL,
    normalized_weight DOUBLE PRECISION NOT NULL,
    displacement_eligible_base NUMERIC(20, 2) NOT NULL,
    displacement_coefficient DOUBLE PRECISION NOT NULL,
    displaced_sales NUMERIC(20, 2) NOT NULL,
    displaced_taxable_sales NUMERIC(20, 2) NOT NULL,
    displaced_business_income NUMERIC(20, 2) NOT NULL,
    sales_tax_loss NUMERIC(20, 2) NOT NULL,
    business_income_tax_loss NUMERIC(20, 2) NOT NULL,
    displaced_jobs DOUBLE PRECISION NOT NULL,
    UNIQUE (model_run_id, scope_kind, scope_code, sector_key),
    CHECK (normalized_weight >= 0 AND normalized_weight <= 1),
    CHECK (displacement_eligible_base >= 0),
    CHECK (displacement_coefficient >= 0 AND displacement_coefficient <= 1),
    CHECK (displaced_sales >= 0),
    CHECK (displaced_taxable_sales >= 0),
    CHECK (displaced_business_income >= 0),
    CHECK (sales_tax_loss >= 0),
    CHECK (business_income_tax_loss >= 0),
    CHECK (displaced_jobs >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_employment_impacts (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    scope_kind VARCHAR(40) NOT NULL,
    scope_code VARCHAR(160) NOT NULL,
    direct_casino_jobs DOUBLE PRECISION NOT NULL,
    construction_job_years DOUBLE PRECISION NOT NULL,
    indirect_and_induced_jobs DOUBLE PRECISION NOT NULL,
    displaced_sector_jobs DOUBLE PRECISION NOT NULL,
    incumbent_casino_jobs_lost DOUBLE PRECISION NOT NULL,
    net_permanent_jobs DOUBLE PRECISION NOT NULL,
    direct_labor_income NUMERIC(20, 2) NOT NULL,
    indirect_labor_income NUMERIC(20, 2) NOT NULL,
    incumbent_labor_income_lost NUMERIC(20, 2) NOT NULL,
    UNIQUE (model_run_id, scope_kind, scope_code),
    CHECK (direct_casino_jobs >= 0),
    CHECK (construction_job_years >= 0),
    CHECK (indirect_and_induced_jobs >= 0),
    CHECK (displaced_sector_jobs >= 0),
    CHECK (incumbent_casino_jobs_lost >= 0),
    CHECK (direct_labor_income >= 0),
    CHECK (indirect_labor_income >= 0),
    CHECK (incumbent_labor_income_lost >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_fiscal_impacts (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    scope_kind VARCHAR(40) NOT NULL,
    scope_code VARCHAR(160) NOT NULL,
    gross_gaming_tax NUMERIC(20, 2) NOT NULL,
    host_local_gross_public_revenue NUMERIC(20, 2) NOT NULL,
    host_state_gross_public_revenue NUMERIC(20, 2) NOT NULL,
    displaced_local_fiscal_loss NUMERIC(20, 2) NOT NULL,
    host_incumbent_gaming_tax_loss NUMERIC(20, 2) NOT NULL,
    other_jurisdiction_gaming_tax_loss NUMERIC(20, 2) NOT NULL,
    net_host_local_fiscal_impact NUMERIC(20, 2) NOT NULL,
    net_host_state_fiscal_impact NUMERIC(20, 2) NOT NULL,
    other_jurisdiction_fiscal_impact NUMERIC(20, 2) NOT NULL,
    rule_provenance_json JSONB NOT NULL DEFAULT '{}'::jsonb,
    UNIQUE (model_run_id, scope_kind, scope_code),
    CHECK (gross_gaming_tax >= 0),
    CHECK (host_local_gross_public_revenue >= 0),
    CHECK (host_state_gross_public_revenue >= 0),
    CHECK (displaced_local_fiscal_loss >= 0),
    CHECK (host_incumbent_gaming_tax_loss >= 0),
    CHECK (other_jurisdiction_gaming_tax_loss >= 0)
);

CREATE TABLE IF NOT EXISTS model_run_social_costs (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    scope_kind VARCHAR(40) NOT NULL,
    scope_code VARCHAR(160) NOT NULL,
    domain_key VARCHAR(100) NOT NULL,
    exposed_eligible_population DOUBLE PRECISION NOT NULL,
    incremental_cases DOUBLE PRECISION NOT NULL,
    per_case_cost NUMERIC(20, 2) NOT NULL,
    annual_cost NUMERIC(20, 2) NOT NULL,
    low_annual_cost NUMERIC(20, 2) NOT NULL,
    high_annual_cost NUMERIC(20, 2) NOT NULL,
    included BOOLEAN NOT NULL DEFAULT TRUE,
    provenance_notes TEXT NULL,
    UNIQUE (model_run_id, scope_kind, scope_code, domain_key),
    CHECK (exposed_eligible_population >= 0),
    CHECK (incremental_cases >= 0),
    CHECK (per_case_cost >= 0),
    CHECK (annual_cost >= 0),
    CHECK (low_annual_cost >= 0),
    CHECK (high_annual_cost >= low_annual_cost)
);

CREATE TABLE IF NOT EXISTS model_run_net_impacts (
    id BIGSERIAL PRIMARY KEY,
    model_run_id UUID NOT NULL REFERENCES model_runs(id) ON DELETE CASCADE,
    scope_kind VARCHAR(40) NOT NULL,
    scope_code VARCHAR(160) NOT NULL,
    gross_property_ggr NUMERIC(20, 2) NOT NULL,
    transfer_effect_ggr NUMERIC(20, 2) NOT NULL,
    cross_jurisdiction_imported_ggr NUMERIC(20, 2) NOT NULL,
    outside_or_unmodeled_leakage_capture NUMERIC(20, 2) NOT NULL,
    induced_resident_ggr NUMERIC(20, 2) NOT NULL,
    tourism_and_traffic_import_ggr NUMERIC(20, 2) NOT NULL,
    local_discretionary_displacement NUMERIC(20, 2) NOT NULL,
    direct_and_indirect_labor_income NUMERIC(20, 2) NOT NULL,
    net_host_local_fiscal_impact NUMERIC(20, 2) NOT NULL,
    net_host_state_fiscal_impact NUMERIC(20, 2) NOT NULL,
    gross_social_cost NUMERIC(20, 2) NOT NULL,
    net_new_local_gaming_activity NUMERIC(20, 2) NOT NULL,
    net_host_local_impact NUMERIC(20, 2) NOT NULL,
    net_host_state_impact NUMERIC(20, 2) NOT NULL,
    accounting_method_key VARCHAR(80) NOT NULL,
    UNIQUE (model_run_id, scope_kind, scope_code),
    CHECK (gross_property_ggr >= 0),
    CHECK (transfer_effect_ggr >= 0),
    CHECK (cross_jurisdiction_imported_ggr >= 0),
    CHECK (outside_or_unmodeled_leakage_capture >= 0),
    CHECK (induced_resident_ggr >= 0),
    CHECK (tourism_and_traffic_import_ggr >= 0),
    CHECK (local_discretionary_displacement >= 0),
    CHECK (direct_and_indirect_labor_income >= 0),
    CHECK (gross_social_cost >= 0)
);

DROP TRIGGER IF EXISTS trg_prevent_finalized_geographic_accounting_mutation
    ON model_run_geographic_accounting;
CREATE TRIGGER trg_prevent_finalized_geographic_accounting_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_geographic_accounting
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_sector_displacement_mutation
    ON model_run_sector_displacement;
CREATE TRIGGER trg_prevent_finalized_sector_displacement_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_sector_displacement
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_employment_impact_mutation
    ON model_run_employment_impacts;
CREATE TRIGGER trg_prevent_finalized_employment_impact_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_employment_impacts
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_fiscal_impact_mutation
    ON model_run_fiscal_impacts;
CREATE TRIGGER trg_prevent_finalized_fiscal_impact_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_fiscal_impacts
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_social_cost_mutation
    ON model_run_social_costs;
CREATE TRIGGER trg_prevent_finalized_social_cost_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_social_costs
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();

DROP TRIGGER IF EXISTS trg_prevent_finalized_net_impact_mutation
    ON model_run_net_impacts;
CREATE TRIGGER trg_prevent_finalized_net_impact_mutation
BEFORE INSERT OR UPDATE OR DELETE ON model_run_net_impacts
FOR EACH ROW EXECUTE FUNCTION prevent_finalized_model_run_child_mutation();
