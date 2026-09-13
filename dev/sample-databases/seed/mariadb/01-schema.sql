-- Sample schema for CCReimagined, MySQL flavour.
-- Mirrors the PostgreSQL seed so the same tables can be compared across engines.

CREATE TABLE member (
    member_id     INT AUTO_INCREMENT PRIMARY KEY,
    last_name     VARCHAR(40)  NOT NULL,
    first_name    VARCHAR(40),
    date_of_birth DATE,
    joined_at     DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    balance       DECIMAL(12,2) NOT NULL DEFAULT 0,
    -- MySQL has no boolean: TINYINT(1) is the convention, and the provider maps it back.
    is_active     TINYINT(1)   NOT NULL DEFAULT 1,
    program_code  VARCHAR(8),
    notes         TEXT,
    photo         BLOB,
    external_ref  CHAR(36)
);

CREATE TABLE claim (
    claim_id   INT AUTO_INCREMENT PRIMARY KEY,
    member_id  INT NOT NULL,
    amount     DECIMAL(10,2) NOT NULL,
    paid_on    DATE,
    memo       VARCHAR(200),
    CONSTRAINT fk_claim_member FOREIGN KEY (member_id) REFERENCES member(member_id)
);

-- No AUTO_INCREMENT: the tool has to guess a key and say so.
CREATE TABLE legacy_lookup (
    lookup_code VARCHAR(10) PRIMARY KEY,
    description VARCHAR(100) NOT NULL,
    sort_order  SMALLINT
);

CREATE TABLE member_program (
    member_id    INT NOT NULL,
    program_code VARCHAR(8) NOT NULL,
    enrolled_on  DATE NOT NULL,
    PRIMARY KEY (member_id, program_code)
);

CREATE TABLE awkward_names (
    id                  INT AUTO_INCREMENT PRIMARY KEY,
    `int`               INT,
    `class`             VARCHAR(20),
    `Unit Cost`         DECIMAL(10,2),
    `ConnectionString`  VARCHAR(50),
    `INTERVIEWER`       VARCHAR(40),
    `2nd_address`       VARCHAR(60)
);

CREATE TABLE inventory (
    inventory_id INT AUTO_INCREMENT PRIMARY KEY,
    sku          VARCHAR(24) NOT NULL,
    quantity     INT NOT NULL DEFAULT 0,
    unit_cost    DECIMAL(10,2) NOT NULL DEFAULT 0,
    total_value  DECIMAL(12,2) AS (quantity * unit_cost) STORED
);

CREATE TABLE type_zoo (
    id            INT AUTO_INCREMENT PRIMARY KEY,
    c_tinyint     TINYINT,
    c_bool        TINYINT(1),
    c_smallint    SMALLINT,
    c_mediumint   MEDIUMINT,
    c_int         INT,
    c_bigint      BIGINT,
    c_uint        INT UNSIGNED,
    c_float       FLOAT,
    c_double      DOUBLE,
    c_decimal     DECIMAL(18,4),
    c_char        CHAR(10),
    c_varchar     VARCHAR(50),
    c_tinytext    TINYTEXT,
    c_text        TEXT,
    c_mediumtext  MEDIUMTEXT,
    c_longtext    LONGTEXT,
    c_json        JSON,
    c_enum        ENUM('one','two','three'),
    c_date        DATE,
    c_time        TIME,
    c_datetime    DATETIME,
    c_timestamp   TIMESTAMP NULL,
    c_year        YEAR,
    c_binary      BINARY(16),
    c_varbinary   VARBINARY(255),
    c_blob        BLOB,
    c_bit1        BIT(1)
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
