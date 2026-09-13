-- Sample schema for CCReimagined, SQLite flavour.
-- SQLite needs no server, so this is applied by ./make-sqlite.sh into a local file.

PRAGMA foreign_keys = ON;

CREATE TABLE member (
    -- INTEGER PRIMARY KEY aliases the rowid, which is the only thing that auto-numbers.
    member_id     INTEGER PRIMARY KEY,
    last_name     VARCHAR(40)   NOT NULL,
    first_name    VARCHAR(40),
    date_of_birth DATE,
    joined_at     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    balance       DECIMAL(12,2) NOT NULL DEFAULT 0,
    is_active     BOOLEAN       NOT NULL DEFAULT 1,
    program_code  VARCHAR(8),
    notes         TEXT,
    photo         BLOB,
    external_ref  GUID
);

CREATE TABLE claim (
    claim_id  INTEGER PRIMARY KEY,
    member_id INTEGER NOT NULL REFERENCES member(member_id),
    amount    DECIMAL(10,2) NOT NULL,
    paid_on   DATE,
    memo      VARCHAR(200)
);

-- A TEXT primary key does not auto-number, so the tool has to guess and say so.
CREATE TABLE legacy_lookup (
    lookup_code VARCHAR(10) PRIMARY KEY,
    description VARCHAR(100) NOT NULL,
    sort_order  SMALLINT
);

CREATE TABLE member_program (
    member_id    INTEGER NOT NULL REFERENCES member(member_id),
    program_code VARCHAR(8) NOT NULL,
    enrolled_on  DATE NOT NULL,
    PRIMARY KEY (member_id, program_code)
);

CREATE TABLE awkward_names (
    id                 INTEGER PRIMARY KEY,
    "int"              INTEGER,
    "class"            VARCHAR(20),
    "Unit Cost"        DECIMAL(10,2),
    "ConnectionString" VARCHAR(50),
    "INTERVIEWER"      VARCHAR(40),
    "2nd_address"      VARCHAR(60)
);

CREATE TABLE inventory (
    inventory_id INTEGER PRIMARY KEY,
    sku          VARCHAR(24) NOT NULL,
    quantity     INTEGER NOT NULL DEFAULT 0,
    unit_cost    DECIMAL(10,2) NOT NULL DEFAULT 0,
    total_value  DECIMAL(12,2) GENERATED ALWAYS AS (quantity * unit_cost) STORED
);

-- WITHOUT ROWID: even an INTEGER PRIMARY KEY does not auto-number here, which is the
-- distinction the SQLite provider has to make.
-- An INTEGER PRIMARY KEY normally aliases the rowid and auto-numbers. In a WITHOUT ROWID
-- table it does not, and that is the distinction the SQLite provider has to make.
CREATE TABLE settings (
    setting_id    INTEGER NOT NULL,
    setting_key   VARCHAR(50) NOT NULL,
    setting_value VARCHAR(200),
    PRIMARY KEY (setting_id)
) WITHOUT ROWID;

CREATE TABLE type_zoo (
    id          INTEGER PRIMARY KEY,
    c_int       INTEGER,
    c_bigint    BIGINT,
    c_smallint  SMALLINT,
    c_real      REAL,
    c_double    DOUBLE,
    c_decimal   DECIMAL(18,4),
    c_text      TEXT,
    c_varchar   VARCHAR(50),
    c_char      CHARACTER(10),
    c_clob      CLOB,
    c_blob      BLOB,
    c_boolean   BOOLEAN,
    c_date      DATE,
    c_datetime  DATETIME,
    c_guid      GUID,
    c_untyped
);

CREATE VIEW active_member AS
    SELECT member_id, last_name, first_name, balance, program_code
    FROM member
    WHERE is_active = 1;

INSERT INTO member (last_name, first_name, date_of_birth, balance, is_active, program_code, notes)
VALUES ('Watson',  'Leigh', '1979-04-12', 125.50, 1, 'ALPHA', 'Seed row.'),
       ('Okonkwo', 'Ada',   '1988-11-02', 0.00,   1, 'BETA',  NULL),
       ('Vasquez', NULL,     NULL,       -42.75,  0, NULL,    NULL);

INSERT INTO claim (member_id, amount, paid_on, memo)
VALUES (1, 60.00, '2026-01-15', 'Initial claim'),
       (1, 65.50, NULL,          NULL),
       (2, 12.25, '2026-02-01', 'Partial');

INSERT INTO legacy_lookup (lookup_code, description, sort_order)
VALUES ('ALPHA', 'Alpha program', 1),
       ('BETA',  'Beta program',  2);

INSERT INTO member_program (member_id, program_code, enrolled_on)
VALUES (1, 'ALPHA', '2025-06-01'),
       (2, 'BETA',  '2025-09-15');

INSERT INTO inventory (sku, quantity, unit_cost)
VALUES ('SKU-001', 10, 4.25),
       ('SKU-002',  3, 19.99);

INSERT INTO settings (setting_id, setting_key, setting_value)
VALUES (1, 'theme', 'dark'), (2, 'retention_days', '90');
