-- Sample schema for CCReimagined, PostgreSQL flavour.
--
-- Deliberately covers the cases the generator has to get right rather than a tidy
-- textbook model: generated keys of both spellings, a table with no generated key at
-- all, column names that are illegal or ambiguous in C#, generated columns, and a view.

CREATE TABLE member (
    member_id     integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    last_name     varchar(40)  NOT NULL,
    first_name    varchar(40),
    date_of_birth date,
    joined_at     timestamptz  NOT NULL DEFAULT now(),
    balance       numeric(12,2) NOT NULL DEFAULT 0,
    is_active     boolean      NOT NULL DEFAULT true,
    program_code  varchar(8),
    notes         text,
    photo         bytea,
    external_ref  uuid
);

-- serial rather than IDENTITY: the older spelling, detected via the nextval() default.
CREATE TABLE claim (
    claim_id   serial PRIMARY KEY,
    member_id  integer NOT NULL REFERENCES member(member_id),
    amount     numeric(10,2) NOT NULL,
    paid_on    date,
    memo       varchar(200)
);

-- No generated key: the tool has to guess one and say so rather than refuse.
CREATE TABLE legacy_lookup (
    lookup_code varchar(10) PRIMARY KEY,
    description varchar(100) NOT NULL,
    sort_order  smallint
);

-- Composite primary key: no single column is the key.
CREATE TABLE member_program (
    member_id    integer NOT NULL REFERENCES member(member_id),
    program_code varchar(8) NOT NULL,
    enrolled_on  date NOT NULL,
    PRIMARY KEY (member_id, program_code)
);

-- Column names that need careful handling on the way to C#.
CREATE TABLE awkward_names (
    id                integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "int"             integer,            -- exactly a C# keyword
    "class"           varchar(20),        -- ditto
    "Unit Cost"       numeric(10,2),      -- a space
    "ConnectionString" varchar(50),       -- collides with a member the class defines
    "INTERVIEWER"     varchar(40),        -- merely contains "int"; must be left alone
    "2nd_address"     varchar(60)         -- leading digit
);

-- A stored generated column must be read but never written.
CREATE TABLE inventory (
    inventory_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sku          varchar(24) NOT NULL,
    quantity     integer NOT NULL DEFAULT 0,
    unit_cost    numeric(10,2) NOT NULL DEFAULT 0,
    total_value  numeric(12,2) GENERATED ALWAYS AS (quantity * unit_cost) STORED
);

-- Broad type coverage, to see the whole mapping table at once.
CREATE TABLE type_zoo (
    id              integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    c_smallint      smallint,
    c_integer       integer,
    c_bigint        bigint,
    c_real          real,
    c_double        double precision,
    c_numeric       numeric(18,4),
    c_money         money,
    c_varchar       varchar(50),
    c_char          char(10),
    c_text          text,
    c_json          json,
    c_jsonb         jsonb,
    c_uuid          uuid,
    c_date          date,
    c_time          time,
    c_timestamp     timestamp,
    c_timestamptz   timestamptz,
    c_interval      interval,
    c_bytea         bytea,
    c_boolean       boolean
);

CREATE VIEW active_member AS
    SELECT member_id, last_name, first_name, balance, program_code
    FROM member
    WHERE is_active;

INSERT INTO member (last_name, first_name, date_of_birth, balance, is_active, program_code, notes)
VALUES
    ('Watson',   'Leigh',  DATE '1979-04-12', 125.50, true,  'ALPHA', 'Seed row.'),
    ('Okonkwo',  'Ada',    DATE '1988-11-02', 0.00,   true,  'BETA',  NULL),
    ('Vasquez',  NULL,     NULL,              -42.75, false, NULL,    NULL);

INSERT INTO claim (member_id, amount, paid_on, memo)
VALUES (1, 60.00, DATE '2026-01-15', 'Initial claim'),
       (1, 65.50, NULL,              NULL),
       (2, 12.25, DATE '2026-02-01', 'Partial');

INSERT INTO legacy_lookup (lookup_code, description, sort_order)
VALUES ('ALPHA', 'Alpha program', 1),
       ('BETA',  'Beta program',  2);

INSERT INTO member_program (member_id, program_code, enrolled_on)
VALUES (1, 'ALPHA', DATE '2025-06-01'),
       (2, 'BETA',  DATE '2025-09-15');

INSERT INTO inventory (sku, quantity, unit_cost)
VALUES ('SKU-001', 10, 4.25),
       ('SKU-002',  3, 19.99);
