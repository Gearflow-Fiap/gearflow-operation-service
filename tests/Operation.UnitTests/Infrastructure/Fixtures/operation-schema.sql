DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'operation') THEN
        CREATE SCHEMA operation;
    END IF;
END $EF$;


CREATE TABLE operation.broker_inbox (
    message_id uuid NOT NULL,
    correlation_id uuid NOT NULL,
    fingerprint character varying(64) NOT NULL,
    command_name character varying(100) NOT NULL,
    trace_parent character varying(55),
    trace_state character varying(512),
    is_completed boolean NOT NULL,
    received_at timestamp with time zone NOT NULL,
    processed_at timestamp with time zone,
    reply_id uuid,
    reply_type character varying(100),
    reply_json jsonb,
    published_at timestamp with time zone,
    CONSTRAINT "PK_broker_inbox" PRIMARY KEY (message_id),
    CONSTRAINT ck_broker_inbox_hash CHECK (length(fingerprint) = 64)
);


CREATE TABLE operation.catalog_jobs (
    id uuid NOT NULL,
    name character varying(128) NOT NULL,
    description character varying(2000) NOT NULL,
    price_cents integer NOT NULL,
    is_active boolean NOT NULL,
    version bigint NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    updated_by character varying(200) NOT NULL,
    CONSTRAINT "PK_catalog_jobs" PRIMARY KEY (id),
    CONSTRAINT ck_catalog_job CHECK (price_cents >= 0 AND version > 0 AND length(trim(name)) > 0)
);


CREATE TABLE operation.operations (
    service_order_id uuid NOT NULL,
    service_order_code integer,
    vehicle_id uuid,
    status character varying(40) NOT NULL,
    is_active boolean NOT NULL,
    is_tombstone boolean NOT NULL,
    version bigint NOT NULL,
    state_format integer NOT NULL,
    state_json jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    updated_at timestamp with time zone NOT NULL,
    diagnostic_started_at timestamp with time zone,
    diagnostic_completed_at timestamp with time zone,
    execution_authorized_at timestamp with time zone,
    execution_started_at timestamp with time zone,
    execution_completed_at timestamp with time zone,
    CONSTRAINT "PK_operations" PRIMARY KEY (service_order_id),
    CONSTRAINT ck_operation_identity CHECK (service_order_id <> '00000000-0000-0000-0000-000000000000'),
    CONSTRAINT ck_operation_tombstone CHECK (NOT is_tombstone OR (NOT is_active AND status = 'Canceled' AND vehicle_id IS NULL)),
    CONSTRAINT ck_operation_version CHECK (version > 0 AND state_format = 1)
);


CREATE TABLE operation.command_receipts (
    intent_id uuid NOT NULL,
    service_order_id uuid NOT NULL,
    fingerprint character varying(64) NOT NULL,
    result_json jsonb NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT "PK_command_receipts" PRIMARY KEY (intent_id),
    CONSTRAINT ck_receipt_fingerprint CHECK (length(fingerprint) = 64),
    CONSTRAINT "FK_command_receipts_operations_service_order_id" FOREIGN KEY (service_order_id) REFERENCES operation.operations (service_order_id) ON DELETE RESTRICT
);


CREATE TABLE operation.histories (
    service_order_id uuid NOT NULL,
    version bigint NOT NULL,
    status character varying(40) NOT NULL,
    action character varying(40) NOT NULL,
    actor text NOT NULL,
    at timestamp with time zone NOT NULL,
    description text NOT NULL,
    subject_id uuid,
    CONSTRAINT "PK_histories" PRIMARY KEY (service_order_id, version),
    CONSTRAINT ck_history_version CHECK (version > 0),
    CONSTRAINT "FK_histories_operations_service_order_id" FOREIGN KEY (service_order_id) REFERENCES operation.operations (service_order_id) ON DELETE RESTRICT
);


CREATE TABLE operation.outbox_events (
    event_id uuid NOT NULL,
    service_order_id uuid NOT NULL,
    causation_id uuid NOT NULL,
    version bigint NOT NULL,
    kind character varying(40) NOT NULL,
    event_json jsonb NOT NULL,
    occurred_at timestamp with time zone NOT NULL,
    dispatched_at timestamp with time zone,
    CONSTRAINT "PK_outbox_events" PRIMARY KEY (event_id),
    CONSTRAINT "FK_outbox_events_command_receipts_causation_id" FOREIGN KEY (causation_id) REFERENCES operation.command_receipts (intent_id) ON DELETE RESTRICT,
    CONSTRAINT "FK_outbox_events_operations_service_order_id" FOREIGN KEY (service_order_id) REFERENCES operation.operations (service_order_id) ON DELETE RESTRICT
);


CREATE INDEX "IX_broker_inbox_is_completed_received_at" ON operation.broker_inbox (is_completed, received_at);


CREATE INDEX "IX_broker_inbox_processed_at_message_id" ON operation.broker_inbox (processed_at, message_id) WHERE reply_json IS NOT NULL AND published_at IS NULL AND is_completed;


CREATE INDEX "IX_catalog_jobs_is_active_name_id" ON operation.catalog_jobs (is_active, name, id);


CREATE INDEX "IX_command_receipts_service_order_id" ON operation.command_receipts (service_order_id);


CREATE INDEX "IX_operations_created_at_service_order_id" ON operation.operations (created_at, service_order_id);


CREATE INDEX "IX_operations_is_active_status_created_at_service_order_id" ON operation.operations (is_active, status, created_at, service_order_id);


CREATE INDEX "IX_outbox_events_causation_id" ON operation.outbox_events (causation_id);


CREATE INDEX "IX_outbox_events_occurred_at_event_id" ON operation.outbox_events (occurred_at, event_id) WHERE dispatched_at IS NULL;


CREATE UNIQUE INDEX "IX_outbox_events_service_order_id_version" ON operation.outbox_events (service_order_id, version);
