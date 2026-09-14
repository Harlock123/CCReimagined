-- Sample schema for CCReimagined, Oracle flavour.
-- Mirrors the other engines. Apply it with ./seed-oracle.sh.

CREATE TABLE member (
    member_id     NUMBER(9)     GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    last_name     VARCHAR2(40)  NOT NULL,
    first_name    VARCHAR2(40),
    date_of_birth DATE,
    joined_at     TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,
    balance       NUMBER(12,2)  DEFAULT 0 NOT NULL,
    -- NUMBER(1) is how a boolean is spelled before 23ai; the provider maps it back.
    is_active     NUMBER(1)     DEFAULT 1 NOT NULL,
    program_code  VARCHAR2(8),
    notes         CLOB,
    photo         BLOB,
    external_ref  RAW(16)
);

CREATE TABLE claim (
    claim_id  NUMBER(9) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    member_id NUMBER(9) NOT NULL REFERENCES member(member_id),
    amount    NUMBER(10,2) NOT NULL,
    paid_on   DATE,
    memo      VARCHAR2(200)
);

-- No identity column: the tool has to guess a key and say so.
CREATE TABLE legacy_lookup (
    lookup_code VARCHAR2(10) PRIMARY KEY,
    description VARCHAR2(100) NOT NULL,
    sort_order  NUMBER(4)
);

CREATE TABLE member_program (
    member_id    NUMBER(9) NOT NULL REFERENCES member(member_id),
    program_code VARCHAR2(8) NOT NULL,
    enrolled_on  DATE NOT NULL,
    CONSTRAINT pk_member_program PRIMARY KEY (member_id, program_code)
);

-- Oracle folds unquoted names to upper case, so these are quoted to keep the
-- spelling the catalog will report.
CREATE TABLE awkward_names (
    id                 NUMBER(9) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "int"              NUMBER(9),
    "class"            VARCHAR2(20),
    "Unit Cost"        NUMBER(10,2),
    "ConnectionString" VARCHAR2(50),
    "INTERVIEWER"      VARCHAR2(40)
);

-- A virtual column is readable and never writable.
CREATE TABLE inventory (
    inventory_id NUMBER(9) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    sku          VARCHAR2(24) NOT NULL,
    quantity     NUMBER(9) DEFAULT 0 NOT NULL,
    unit_cost    NUMBER(10,2) DEFAULT 0 NOT NULL,
    total_value  NUMBER(12,2) GENERATED ALWAYS AS (quantity * unit_cost) VIRTUAL
);

CREATE TABLE type_zoo (
    id            NUMBER(9) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    c_num1        NUMBER(1),
    c_num4        NUMBER(4),
    c_num9        NUMBER(9),
    c_num18       NUMBER(18),
    c_num_scaled  NUMBER(18,4),
    c_number      NUMBER,
    c_integer     INTEGER,
    c_float       BINARY_FLOAT,
    c_double      BINARY_DOUBLE,
    c_varchar2    VARCHAR2(50),
    c_nvarchar2   NVARCHAR2(50),
    c_char        CHAR(10),
    c_clob        CLOB,
    c_nclob       NCLOB,
    c_blob        BLOB,
    c_raw         RAW(16),
    c_date        DATE,
    c_timestamp   TIMESTAMP,
    c_timestamptz TIMESTAMP WITH TIME ZONE,
    c_interval    INTERVAL DAY TO SECOND
);

CREATE VIEW active_member AS
    SELECT member_id, last_name, first_name, balance, program_code
    FROM member
    WHERE is_active = 1;

-- An aggregating view, which Oracle reports as not updatable.
CREATE VIEW member_counts AS
    SELECT program_code, COUNT(*) AS how_many
    FROM member
    GROUP BY program_code;

INSERT INTO member (last_name, first_name, date_of_birth, balance, is_active, program_code, notes)
VALUES ('Watson', 'Leigh', DATE '1979-04-12', 125.50, 1, 'ALPHA', 'Seed row.');
INSERT INTO member (last_name, first_name, date_of_birth, balance, is_active, program_code, notes)
VALUES ('Okonkwo', 'Ada', DATE '1988-11-02', 0, 1, 'BETA', NULL);
INSERT INTO member (last_name, first_name, date_of_birth, balance, is_active, program_code, notes)
VALUES ('Vasquez', NULL, NULL, -42.75, 0, NULL, NULL);

INSERT INTO legacy_lookup (lookup_code, description, sort_order) VALUES ('ALPHA', 'Alpha program', 1);
INSERT INTO legacy_lookup (lookup_code, description, sort_order) VALUES ('BETA', 'Beta program', 2);

INSERT INTO inventory (sku, quantity, unit_cost) VALUES ('SKU-001', 10, 4.25);
INSERT INTO inventory (sku, quantity, unit_cost) VALUES ('SKU-002', 3, 19.99);

COMMIT;
