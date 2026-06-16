-- serial_transmitter

library ieee;
use ieee.std_logic_1164.all;
use ieee.numeric_std.all;

entity SERIAL_TRANSMITTER is
    port
    (
        CLK     : in    std_logic;                           -- clock
        RESET   : in    std_logic;                           -- reset
        LOAD    : in    std_logic;                           -- lade
        DATA    : in    std_logic_vector(7 downto 0);        -- Daten
        BUSY    : out   std_logic;                           -- beschaeftigt
        LINEOUT : out   std_logic                            -- Ausgang
    );
end SERIAL_TRANSMITTER;

architecture BEHAVE of SERIAL_TRANSMITTER is
begin
    process(RESET, CLK, LOAD, DATA) is
        -- DEFINE A STATE-TYPE
        type TSTATE is(
            S_IDLE,  -- Ruhe
            S_LOAD,  -- Lade
            S_START, -- Starte
            S_SEND,  -- Sende
            S_STOP   -- Stoppe
        );
        variable STATE : TSTATE;
        -- VARIABLES
        variable CNT : integer range 0 to 255;              -- Zaehler
        variable D   : std_logic_vector(7 downto 0);        -- Data (shifted)
    begin
        if RESET='1' then
            STATE := S_IDLE;
        elsif CLK'event and CLK='1' then
            -- STATE-TRANSITION-FUNCTION
            case STATE is
                when S_IDLE =>
                    d := "00000000"; -- variable assignment
                    if(LOAD='1') then
                        STATE := S_LOAD;
                    end if;
                when S_LOAD =>
                    d := DATA; -- variable assignment
                    if(LOAD='0') then
                        STATE := S_START;
                    end if;
                when S_START =>
                    cnt := 0; -- variable assignment
                    if(true) then
                        STATE := S_SEND;
                    end if;
                when S_SEND =>
                    d := '0' & D(7 downto 1); -- variable assignment
                    cnt := cnt + 1; -- variable assignment
                    if(CNT=7) then
                        STATE := S_STOP;
                    end if;
                when S_STOP =>
                    if(true) then
                        STATE := S_IDLE;
                    end if;
                when others =>
                    STATE := S_IDLE;
            end case;
            -- OUTPUT-FUNCTION
            case STATE is
                when S_IDLE =>
                    BUSY    <= '0';
                    LINEOUT <= '1';
                when S_LOAD =>
                    BUSY    <= '0';
                    LINEOUT <= '1';
                when S_START =>
                    BUSY    <= '1';
                    LINEOUT <= '0';
                when S_SEND =>
                    BUSY    <= '1';
                    LINEOUT <= D(0);
                when S_STOP =>
                    BUSY    <= '1';
                    LINEOUT <= '1';
            end case;
        end if;
    end process;

end BEHAVE;
